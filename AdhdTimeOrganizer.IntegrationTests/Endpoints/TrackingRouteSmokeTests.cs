using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.desktop;
using AdhdTimeOrganizer.Tracking.infrastructure.jobs;
using AdhdTimeOrganizer.Tracking.infrastructure.scheduling;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sydowwe.Framework.Contracts.scheduling;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.infrastructure.persistence;
using Sydowwe.Framework.Testing;
using Sydowwe.Scheduler.domain.entity;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// The Tracking counterpart of <see cref="CoreRouteSmokeTests"/> / <c>HistoryRouteSmokeTests</c> /
/// <c>PlanningRouteSmokeTests</c>, plus the two model-level checks this slice specifically needs.
/// <para>
/// The shared trap first: these endpoints only route if <c>AdhdTimeOrganizer.Tracking</c> is in the
/// FastEndpoints <c>o.Assemblies</c> list in <c>Program.cs</c> (<c>DisableAutoDiscovery = true</c>).
/// Leaving it out is not a build error — every tracking route simply 404s.
/// </para>
/// <para>
/// Two traps are specific to Tracking, and both are invisible to a route check.
/// <see cref="WebExtensionActivityEntry_KeepsItsCombinedQueryFilter"/> covers the entity that is
/// <em>excluded</em> from the automatic per-user filter and hand-given a combined one — the one place
/// where losing the filter would not be caught by the general <c>IEntityWithUser</c> rule.
/// <see cref="PartitionedTrackingTables_KeepTheirPartitionKey"/> covers the two partitioned tables,
/// whose partition DDL comes from a generator wired host-side: if the entity configurations stopped
/// being applied, the model would still build and the tables would silently lose their partitioning.
/// </para>
/// <para>
/// The ingest endpoints' auth (extension-client token + the <c>ActivityTracking</c> policy) is covered
/// in <c>ExtensionActivityTrackingTests</c>, which also owns the end-to-end seam case. Nothing is
/// duplicated here.
/// </para>
/// </summary>
[Collection("Postgres")]
public class TrackingRouteSmokeTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// The dashboard POSTs across all three trackers. Asserting on <c>OK</c> rather than "not 404"
    /// avoids the trap that a GET against a POST-only route answers 405, which passes a
    /// <c>NotBe(NotFound)</c> check for entirely the wrong reason — and the larger trap that a body the
    /// endpoint rejects passes it too.
    /// <para>
    /// That larger trap was live here: this case asserted only "not 404 / not 405", and every one of the
    /// twelve routes was answering <b>400</b>, because <c>DashboardBody</c> sent <c>from</c>/<c>to</c> as
    /// <c>DateTime</c> while all twelve requests derive from <c>DateAndTimeRangeDto</c>, whose
    /// <c>From</c>/<c>To</c> are <c>TimeDto</c> (the portal's time-of-day convention). Model binding
    /// failed before any handler ran, so the case proved only that something was bound to each route.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("/api/activity-tracking/desktop/pie-chart")]
    [InlineData("/api/activity-tracking/desktop/stacked-bars")]
    [InlineData("/api/activity-tracking/desktop/summary-cards")]
    [InlineData("/api/activity-tracking/desktop/timeline")]
    [InlineData("/api/activity-tracking/web-extension/pie-chart")]
    [InlineData("/api/activity-tracking/web-extension/stacked-bars")]
    [InlineData("/api/activity-tracking/web-extension/summary-cards")]
    [InlineData("/api/activity-tracking/web-extension/timeline")]
    [InlineData("/api/activity-tracking/android/pie-chart")]
    [InlineData("/api/activity-tracking/android/stacked-bars")]
    [InlineData("/api/activity-tracking/android/summary-cards")]
    [InlineData("/api/activity-tracking/android/timeline")]
    [InlineData("/api/activity-tracking/desktop/focus-metrics")]
    [InlineData("/api/activity-tracking/web-extension/focus-metrics")]
    [InlineData("/api/activity-tracking/android/focus-metrics")]
    public async Task TrackingDashboardRoutes_AreRegistered(string route)
    {
        var response = await CreateUserRoleClient()
            .PostAsJsonAsync(route, DashboardBody(), JsonOpts, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// The two pattern-mapping settings grids and the two distinct-entry grids. These sit under the
    /// <c>settings</c> groups, three folders deep, and are the likeliest routes to be lost in a
    /// folder-driven move.
    /// <para>
    /// Note the two shapes are not the same. The distinct-entry grids are hand-written endpoints that
    /// <c>Post("/gird")</c> straight onto the group prefix (<c>gird</c> is the shipped spelling — see
    /// <c>AdhdTimeOrganizer.History/docs/domain-map.md</c>). The settings grids derive from
    /// <c>BaseGridEndpoint</c>, whose route is <c>{entity-name}/{EndpointPath}</c> with
    /// <c>EndpointPath</c> defaulting to <c>filtered-table</c> — so they answer under the entity name,
    /// not under <c>gird</c>.
    /// </para>
    /// <para>
    /// Asserting on <c>OK</c> rather than "not 404" is what makes this case mean anything, and the two
    /// settings rows are why. They previously passed on a <b>400</b>: <c>GridBody</c> sent
    /// <c>filter: {}</c>, and both mapping filters declare <c>required TrackerDesktopMappingTypeEnum
    /// Type</c>, which System.Text.Json enforces during deserialization — so the request never reached
    /// the handler and the case proved only that <em>something</em> was bound to the route, which a
    /// stray 400 from anywhere would equally have satisfied. See <c>GridBody</c> for why the body now
    /// sends <c>filter: null</c>.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("/api/activity-tracking/desktop/settings/tracker-desktop-mapping-by-pattern/filtered-table")]
    [InlineData("/api/activity-tracking/android/settings/tracker-android-mapping-by-pattern/filtered-table")]
    [InlineData("/api/activity-tracking/desktop/gird")]
    [InlineData("/api/activity-tracking/android/gird")]
    public async Task TrackingGridRoutes_AreRegistered(string route)
    {
        var response = await CreateUserRoleClient()
            .PostAsJsonAsync(route, GridBody(), JsonOpts, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DesktopDistinctProcessesRoute_IsRegistered()
    {
        var response = await CreateUserRoleClient()
            .GetAsync("/api/activity-tracking/desktop/distinct-processes", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// <c>WebExtensionActivityEntry</c> is listed in <c>AppDbContext.UserScopingExcludedTypes</c> and
    /// given a hand-written filter combining the per-user check with
    /// <c>RecordDate &gt;= CurrentPartitionDate</c>. Both halves stay host-side while the entity now
    /// lives in the slice, so nothing about this arrangement is enforced by the compiler: drop the
    /// entity from the exclusion list and it silently gets the generic user-only filter instead,
    /// losing the partition-date bound; drop the hand-written filter and it loses user scoping
    /// entirely, which leaks every user's browsing history to any signed-in caller.
    /// <para>
    /// The two halves are checked differently, and not by choice. Fixture-built contexts are
    /// constructed straight off a <c>DbContextOptionsBuilder</c>, so they have no application service
    /// provider — <c>UserScopingOptions</c> resolves to null and <b>no</b> per-user filter is applied to
    /// any entity in them (see <c>BaseDbContext.ApplyUserScopingIfEnabled</c>). That is what makes them
    /// usable for cross-user seeding in the first place, and it means seeded rows can only demonstrate
    /// the date half. The user half is asserted against a host-built model, where scoping is on.
    /// </para>
    /// </summary>
    [Fact]
    public async Task WebExtensionActivityEntry_KeepsItsCombinedQueryFilter()
    {
        const long otherUserId = FakeLoggedUserService.TestUserId + 9_000;

        await using (var seed = CreateDbContext())
        {
            // The other user needs to exist for the FK; only the id matters.
            if (!await seed.Set<User>().IgnoreQueryFilters().AnyAsync(u => u.Id == otherUserId, CancellationToken))
            {
                seed.Set<User>().Add(new User
                {
                    Id = otherUserId,
                    Email = "webext-filter-other@test.com",
                    NormalizedEmail = "WEBEXT-FILTER-OTHER@TEST.COM",
                    UserName = "webext-filter-other@test.com",
                    NormalizedUserName = "WEBEXT-FILTER-OTHER@TEST.COM",
                    SecurityStamp = Guid.NewGuid().ToString("N"),
                    ConcurrencyStamp = Guid.NewGuid().ToString("N"),
                    Locale = AvailableLocales.En,
                    Timezone = TimeZoneInfo.Utc
                });
                await seed.SaveChangesAsync(CancellationToken);
            }

            seed.Set<WebExtensionActivityEntry>().AddRange(
                NewEntry(FakeLoggedUserService.TestUserId, DateTime.UtcNow, "mine-recent.test"),
                // Before CurrentPartitionDate (UtcNow - 2y), so the date half must hide it.
                NewEntry(FakeLoggedUserService.TestUserId, DateTime.UtcNow.AddYears(-3), "mine-ancient.test"),
                NewEntry(otherUserId, DateTime.UtcNow, "theirs-recent.test"));
            await seed.SaveChangesAsync(CancellationToken);
        }

        await using var db = CreateDbContext();
        var visible = await db.Set<WebExtensionActivityEntry>()
            .Select(e => e.Domain)
            .ToListAsync(CancellationToken);

        visible.Should().Contain("mine-recent.test");
        visible.Should().NotContain("mine-ancient.test",
            "the RecordDate >= CurrentPartitionDate half of the combined filter must survive");

        using var scope = Fixture.UnauthenticatedFactory.Services.CreateScope();
        var hostModel = scope.ServiceProvider.GetRequiredService<AppDbContext>().Model;
        var hostFilter = string.Join(" && ", hostModel.FindEntityType(typeof(WebExtensionActivityEntry))!
            .GetDeclaredQueryFilters()
            .Select(f => f.Expression.ToString()));

        hostFilter.Should().NotBeEmpty("the entity must carry a query filter at all");
        hostFilter.Should().Contain(nameof(WebExtensionActivityEntry.RecordDate),
            "the partition-date bound is the half that would be lost by dropping the entity from " +
            "UserScopingExcludedTypes and letting it take the generic user-only filter");
        hostFilter.Should().Contain(nameof(WebExtensionActivityEntry.UserId),
            "the per-user half must survive -- this entity is excluded from ApplyUserQueryFilters, so if " +
            "the hand-written filter in OnModelCreating regressed to the date bound alone, nothing else " +
            "scopes it and every user's browsing history becomes readable");
    }

    private static WebExtensionActivityEntry NewEntry(long userId, DateTime windowStart, string domain) => new()
    {
        UserId = userId,
        // Minute-aligned: WindowStart is required to be, and RecordDate derives from it.
        WindowStart = new DateTime(windowStart.Year, windowStart.Month, windowStart.Day, windowStart.Hour, windowStart.Minute, 0, DateTimeKind.Utc),
        Domain = domain,
        Url = null,
        ActiveSeconds = 10,
        BackgroundSeconds = 0,
        IsFinal = true
    };

    /// <summary>
    /// <c>DesktopActivityEntry</c> and <c>WebExtensionActivityEntry</c> are RANGE-partitioned on
    /// <c>RecordDate</c> via <c>IsPartitionedByRange</c>. The partition DDL is emitted by
    /// <c>PartitionedNpgsqlMigrationsSqlGenerator</c>, wired host-side in <b>both</b> <c>Program.cs</c>
    /// and <c>AppCommandDbContextFactory</c> — so if the slice's configurations stopped being applied
    /// (a missing <c>ApplyConfigurationsFromAssembly</c> call, say), the model would still build and
    /// the next migration would quietly emit plain, unpartitioned tables.
    /// </summary>
    [Theory]
    [InlineData(typeof(DesktopActivityEntry))]
    [InlineData(typeof(WebExtensionActivityEntry))]
    public void PartitionedTrackingTables_KeepTheirPartitionKey(Type entityType)
    {
        using var db = CreateDbContext();

        var entity = db.Model.FindEntityType(entityType);
        entity.Should().NotBeNull($"{entityType.Name} must be in the model -- if it is null, the " +
                                  "Tracking ApplyConfigurationsFromAssembly call is missing from AppDbContext");

        entity!.FindAnnotation(PartitioningExtensions.AnnotationPartitionColumn)
            .Should().NotBeNull($"{entityType.Name} must keep its range-partition annotation");
    }

    /// <summary>
    /// The retention purge is the only GDPR Art. 5(1)(e) mechanism over these two append-only PII ledgers,
    /// and since it now runs on the Scheduler substrate it needs <b>both</b> halves to be present: the keyed
    /// handler in DI, and the recurring registration <c>TrackingScheduledJobsRegistrar</c> pushes on boot.
    /// Losing either fails silently — no build error, no exception, the rows simply accumulate forever.
    /// A handler with no registration never fires; a registration whose <c>HandlerKey</c> resolves to nothing
    /// fires into a "misconfigured" run-log row nobody reads.
    /// </summary>
    [Fact]
    public async Task RetentionPurgeJob_IsRegisteredAndScheduled()
    {
        using var scope = Fixture.UnauthenticatedFactory.Services.CreateScope();

        scope.ServiceProvider.GetServices<IScheduledJobHandler>()
            .Select(h => h.Key)
            .Should().Contain(PurgeExpiredActivityTrackingEntriesJobHandler.HandlerKey,
                "the dispatcher matches RecurringJobRegistration.HandlerKey against the registered handlers; " +
                "a handler missing from DI is a job that fires into nothing");

        // Boot reconciliation has to be re-observed on a fresh host: the cached fixture factories started
        // before the per-test Respawn wiped their scheduled_job rows -- which is also exactly why the
        // registrar must run on every boot (the Quartz RAM job store drops all triggers on restart).
        await using var factory = CreateFactory(TestRoles.AdminAndUser);
        factory.CreateClient().Dispose();

        await using var db = CreateDbContext();
        var jobKeys = await db.Set<ScheduledJob>().Select(j => j.JobKey).ToListAsync(CancellationToken);

        jobKeys.Should().Contain(TrackingScheduledJobsRegistrar.RetentionPurgeRegistration.JobKey,
            "TrackingScheduledJobsRegistrar must stay in the host's AddHostedService list -- without it the " +
            "purge is never scheduled and the ledger rows accumulate forever");
    }

    // ---- helpers ---------------------------------------------------------

    /// <summary>
    /// Superset body covering the pie-chart / summary-cards / stacked-bars / timeline request shapes.
    /// Extra members are ignored on either side, but every member each route <em>does</em> read has to be
    /// both bindable and valid, because the case now asserts <c>OK</c> — a 400 no longer passes.
    /// <list type="bullet">
    /// <item><c>dateFrom</c>/<c>dateTo</c> are the inclusive day span — <c>required</c> on
    /// <c>DateRangeAndTimeRangeDto</c>, so omitting either is a bind failure rather than a validation
    /// message. Equal here, which every dashboard accepts including the two timelines, which reject a
    /// span outright.</item>
    /// <item><c>from</c>/<c>to</c> are <c>TimeDto</c> (<c>{hours, minutes}</c>), not <c>DateTime</c> —
    /// they are the time-of-day window applied to each day of the span, and every validator here has a
    /// <c>RuleFor(x =&gt; x.From).NotEmpty()</c>.</item>
    /// <item><c>windowMinutes</c> is <c>required</c> on the two stacked-bars requests and is checked
    /// against a fixed set.</item>
    /// <item><c>baseline</c> is deliberately <b>omitted</b>. It is a <c>BaselineType</c> enum validated
    /// with <c>IsInEnum</c>, this file's <c>JsonOpts</c> has no string-enum converter, and the DTO
    /// already defaults it to <c>Last7Days</c> — so sending it as a string is the one way to break it.
    /// (The old body sent <c>"Day"</c>, which is not even a member of the enum.)</item>
    /// </list>
    /// </summary>
    private static object DashboardBody() => new
    {
        dateFrom = DateOnly.FromDateTime(DateTime.UtcNow),
        dateTo = DateOnly.FromDateTime(DateTime.UtcNow),
        from = new { hours = 0, minutes = 0 },
        to = new { hours = 23, minutes = 59 },
        windowMinutes = 30,
        topN = 5,
        minPercent = 1.0,
        minSeconds = 60
    };

    /// <summary>
    /// <c>filter: null</c>, not <c>filter: {}</c>. <c>BaseFilterRequest.Filter</c> is
    /// <c>required TFilter?</c>, so an explicit null satisfies the required-presence check without
    /// deserializing a filter object at all — which is the only body shape all four grids accept. An
    /// empty object is fine for the two distinct-entry filters (every member nullable) but 400s on the
    /// two mapping filters, whose <c>Type</c> is <c>required</c>. Since <c>useFilter</c> is false, none
    /// of the four reaches its <c>ApplyCustomFiltering</c> either way.
    /// </summary>
    private static object GridBody() => new
    {
        useFilter = false,
        filter = (object?)null,
        sortBy = Array.Empty<object>(),
        itemsPerPage = 20,
        page = 1
    };
}
