using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.IntegrationTests.Endpoints;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using AdhdTimeOrganizer.infrastructure.jobs;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.infrastructure.persistence.interceptors;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sydowwe.Framework.Contracts.scheduling;
using Sydowwe.Framework.domain.valueObject;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Infrastructure;

/// <summary>
/// TEST-14 — <c>SuggestionPatternRefreshInterceptor</c> / <c>SuggestionPatternRefreshJobHandler</c>.
/// <para>
/// The original prompt (<c>review/portal/testingPrompts/SuggestionPatternRefreshInterceptor.md</c>) was written
/// against a design where the interceptor itself ran <c>REFRESH MATERIALIZED VIEW CONCURRENTLY</c> synchronously
/// inside <c>SavedChangesAsync</c>, with no <c>SaveChangesFailedAsync</c> override — that is what made CQ-9
/// (a refresh failure surfacing as a 500 on an already-committed save) and CQ-10 (leaked flags queuing a
/// spurious refresh on the next unrelated save) possible.
/// </para>
/// <para>
/// Current <c>main</c> has already been refactored: the interceptor only marks a shared
/// <see cref="ISuggestionPatternRefreshQueue"/> dirty and has a <see cref="SaveChangesInterceptor" /> override
/// gone through cleanly for <c>SaveChangesFailedAsync</c> that resets the flags; the actual
/// <c>REFRESH MATERIALIZED VIEW CONCURRENTLY</c> calls now live in <see cref="SuggestionPatternRefreshJobHandler"/>,
/// off the request thread, with a per-view try/catch that only logs. So CQ-9 / CQ-10 as originally described are
/// already fixed — there is no HTTP-visible failure mode to reproduce, and no scenario is tagged
/// <c>KnownGap</c> here. This file re-targets the prompt's scenarios at the current architecture: interceptor
/// flag correctness (originally Scenario C), the flag-reset guarantee (Scenario B, now already-passing), the job
/// handler's per-view failure isolation (Scenario A's intent, moved to the job), the unique-index precondition
/// (Scenario E), and view-installation coverage (Scenario F — originally a parity check between the two
/// installation paths; the fixture now calls <see cref="SuggestionPatternViewInstaller"/> itself, so there is
/// only one path left and the check is instead enum-vs-scripts). Scenario D (PERF) is skipped — it
/// would only be meaningful against the old synchronous-refresh-on-save design.
/// </para>
/// </summary>
[Collection("Postgres")]
public class SuggestionPatternRefreshTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = PlanningTestSeedHelper.TestUserId;

    private static readonly IReadOnlyDictionary<SuggestionPatternView, string> ViewNames = new Dictionary<SuggestionPatternView, string>
    {
        [SuggestionPatternView.PlannerTask] = "mv_planner_task_pattern",
        [SuggestionPatternView.ActivityHistory] = "mv_activity_history_pattern",
        [SuggestionPatternView.TemplateSuggestion] = "mv_template_suggestion_pattern"
    };

    // ---- helpers ------------------------------------------------------------------------------------

    private async Task<(IServiceProvider Services, AppDbContext Db)> NewHostScopeAsync(IServiceScope scope)
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // Force the connection open so callers can rely on AppDbContext being immediately usable.
        await db.Database.CanConnectAsync(CancellationToken);
        return (scope.ServiceProvider, db);
    }

    private static async Task<ActivityHistory> SeedActivityHistoryRowAsync(
        DbContext db, long activityId, DateTime startUtc, CancellationToken ct)
    {
        var row = new ActivityHistory
        {
            UserId = UserId,
            ActivityId = activityId,
            StartTimestamp = startUtc,
            EndTimestamp = startUtc.AddHours(1),
            Length = new IntTime(3600)
        };
        db.Set<ActivityHistory>().Add(row);
        await db.SaveChangesAsync(ct);
        return row;
    }

    private async Task RunRefreshJobAsync(IServiceProvider services, CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var handler = (IScheduledJobHandler)ActivatorUtilities.CreateInstance(scope.ServiceProvider, typeof(SuggestionPatternRefreshJobHandler));

        var now = DateTime.UtcNow;
        await handler.ExecuteAsync(new ScheduledJobContext
        {
            ScheduledFireTimeUtc = now,
            ActualFireTimeUtc = now,
            JobKey = handler.Key,
            CorrelationId = "suggestion-pattern-refresh-job-test",
            TriggerSource = TriggerSource.Manual
        }, ct);
    }

    private static async Task DropViewAsync(DbContext db, SuggestionPatternView view, CancellationToken ct) =>
        await db.Database.ExecuteSqlRawAsync($"DROP MATERIALIZED VIEW {ViewNames[view]}", ct);

    // Goes through the real installer rather than re-reading the script here: it already skips views that
    // still exist, so restoring one dropped view is exactly "install whatever is missing".
    private async Task RestoreMissingViewsAsync(CancellationToken ct)
    {
        await using var db = fixture.CreateDbContext();
        await SuggestionPatternViewInstaller.EnsureViewsCreatedAsync(db, NullLogger.Instance, ct);
    }

    private static async Task<int> ViewRowCountAsync(DbContext db, SuggestionPatternView view, CancellationToken ct) =>
        await db.Database.SqlQueryRaw<int>($"SELECT COUNT(*)::int AS \"Value\" FROM {ViewNames[view]}").SingleAsync(ct);

    // ---- flag correctness: only the touched view's queue entry is marked --------------------------

    [Fact]
    public async Task Save_PlannerTask_MarksOnlyPlannerTaskDirty()
    {
        await using var factory = CreateFactory(TestRoles.AdminAndUser);
        var queue = factory.Services.GetRequiredService<ISuggestionPatternRefreshQueue>();
        queue.DrainDirty();

        await using var scope = factory.Services.CreateAsyncScope();
        var (_, db) = await NewHostScopeAsync(scope);

        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "planner-flag-activity", ct: CancellationToken);
        var calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, DateOnly.FromDateTime(DateTime.UtcNow), ct: CancellationToken);
        queue.DrainDirty(); // seeding the Activity/Calendar rows must not itself dirty anything.

        await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, calendarId, new TimeOnly(9, 0), new TimeOnly(10, 0), ct: CancellationToken);

        queue.DrainDirty().Should().BeEquivalentTo([SuggestionPatternView.PlannerTask]);
    }

    [Fact]
    public async Task Save_ActivityHistory_MarksOnlyActivityHistoryDirty()
    {
        await using var factory = CreateFactory(TestRoles.AdminAndUser);
        var queue = factory.Services.GetRequiredService<ISuggestionPatternRefreshQueue>();
        queue.DrainDirty();

        await using var scope = factory.Services.CreateAsyncScope();
        var (_, db) = await NewHostScopeAsync(scope);

        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "history-flag-activity", ct: CancellationToken);
        queue.DrainDirty();

        await SeedActivityHistoryRowAsync(db, activityId, DateTime.UtcNow, CancellationToken);

        queue.DrainDirty().Should().BeEquivalentTo([SuggestionPatternView.ActivityHistory]);
    }

    [Fact]
    public async Task Save_Calendar_MarksOnlyTemplateSuggestionDirty()
    {
        await using var factory = CreateFactory(TestRoles.AdminAndUser);
        var queue = factory.Services.GetRequiredService<ISuggestionPatternRefreshQueue>();
        queue.DrainDirty();

        await using var scope = factory.Services.CreateAsyncScope();
        var (_, db) = await NewHostScopeAsync(scope);

        await PlanningTestSeedHelper.SeedCalendarAsync(db, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1), ct: CancellationToken);

        queue.DrainDirty().Should().BeEquivalentTo([SuggestionPatternView.TemplateSuggestion]);
    }

    [Fact]
    public async Task Save_UnrelatedEntity_MarksNothingDirty()
    {
        await using var factory = CreateFactory(TestRoles.AdminAndUser);
        var queue = factory.Services.GetRequiredService<ISuggestionPatternRefreshQueue>();
        queue.DrainDirty();

        await using var scope = factory.Services.CreateAsyncScope();
        var (_, db) = await NewHostScopeAsync(scope);

        await PlanningTestSeedHelper.SeedActivityAsync(db, "unrelated-flag-activity", ct: CancellationToken);

        queue.DrainDirty().Should().BeEmpty("creating an Activity/ActivityRole touches none of the three tracked entity types");
    }

    // ---- flags must not survive a failed save --------------------------------------------------------

    [Fact]
    public async Task FailedSave_DoesNotLeaveFlagsPrimedForTheNextSave()
    {
        await using var factory = CreateFactory(TestRoles.AdminAndUser);
        var queue = factory.Services.GetRequiredService<ISuggestionPatternRefreshQueue>();
        queue.DrainDirty();

        await using var scope = factory.Services.CreateAsyncScope();
        var (_, db) = await NewHostScopeAsync(scope);

        // A PlannerTask with a FK that doesn't exist: SavingChangesAsync still primes the PlannerTask flag
        // (it inspects the change tracker before hitting the DB), then the save itself fails at the FK
        // constraint, so SaveChangesFailedAsync is the only thing standing between this and a leaked flag.
        db.Set<PlannerTask>().Add(new PlannerTask
        {
            UserId = UserId,
            ActivityId = -1,
            CalendarId = -1,
            Status = PlannerTaskStatus.NotStarted,
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(10, 0),
            IsBackground = false
        });

        var act = async () => await db.SaveChangesAsync(CancellationToken);
        await act.Should().ThrowAsync<DbUpdateException>();

        // Same DbContext instance, entity type that must never trigger a refresh on its own.
        db.ChangeTracker.Clear();
        await PlanningTestSeedHelper.SeedActivityAsync(db, "post-failure-activity", ct: CancellationToken);

        queue.DrainDirty().Should().BeEmpty(
            "SaveChangesFailedAsync must reset the flags primed by the failed PlannerTask save, otherwise this unrelated save would leak a spurious PlannerTask refresh");
    }

    // ---- job handler: refreshes only what's dirty, and a broken view doesn't stop the others -------

    [Fact]
    public async Task RefreshJob_OnlyRefreshesViewsMarkedDirty_UndirtiedBrokenViewIsIgnored()
    {
        await using var factory = CreateFactory(TestRoles.AdminAndUser);
        var queue = factory.Services.GetRequiredService<ISuggestionPatternRefreshQueue>();
        queue.DrainDirty();

        await using (var db = CreateDbContext())
            await DropViewAsync(db, SuggestionPatternView.TemplateSuggestion, CancellationToken);

        try
        {
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var (_, db) = await NewHostScopeAsync(scope);
                var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "job-selective-activity", ct: CancellationToken);
                var calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2), ct: CancellationToken);
                queue.DrainDirty(); // the Calendar seed above marks TemplateSuggestion dirty too; only PlannerTask should stay marked.
                await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, calendarId, new TimeOnly(9, 0), new TimeOnly(10, 0), ct: CancellationToken);
            }

            var act = async () => await RunRefreshJobAsync(factory.Services, CancellationToken);
            await act.Should().NotThrowAsync("the job must never touch mv_template_suggestion_pattern since it was never marked dirty, even though it's currently missing");
        }
        finally
        {
            await RestoreMissingViewsAsync(CancellationToken);
        }
    }

    [Fact]
    public async Task RefreshJob_OneViewRefreshFails_OtherDirtyViewsStillRefresh_AndJobDoesNotThrow()
    {
        await using var factory = CreateFactory(TestRoles.AdminAndUser);
        var queue = factory.Services.GetRequiredService<ISuggestionPatternRefreshQueue>();
        queue.DrainDirty();

        long activityId;
        var startUtc = DateTime.UtcNow.Date.AddHours(9);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var (_, db) = await NewHostScopeAsync(scope);
            activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "job-failure-isolation-activity", ct: CancellationToken);
            var calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, DateOnly.FromDateTime(startUtc), ct: CancellationToken);
            queue.DrainDirty();

            // mv_activity_history_pattern's HAVING COUNT(*) >= 3 needs three same-day-of-week rows to surface.
            for (var i = 0; i < 3; i++)
                await SeedActivityHistoryRowAsync(db, activityId, startUtc.AddDays(7 * i), CancellationToken);

            await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, calendarId, new TimeOnly(9, 0), new TimeOnly(10, 0), ct: CancellationToken);
        }

        queue.DrainDirty().Should().BeEquivalentTo([SuggestionPatternView.PlannerTask, SuggestionPatternView.ActivityHistory]);

        // Re-mark both dirty (DrainDirty above cleared them) and break the PlannerTask view before the job runs.
        queue.MarkDirty(SuggestionPatternView.PlannerTask);
        queue.MarkDirty(SuggestionPatternView.ActivityHistory);

        await using (var db = CreateDbContext())
            await DropViewAsync(db, SuggestionPatternView.PlannerTask, CancellationToken);

        try
        {
            var act = async () => await RunRefreshJobAsync(factory.Services, CancellationToken);
            await act.Should().NotThrowAsync("a REFRESH failure on one view (42P01, missing relation) must only be logged, never propagate out of the job");

            await using var verify = CreateDbContext();
            var historyRows = await ViewRowCountAsync(verify, SuggestionPatternView.ActivityHistory, CancellationToken);
            historyRows.Should().BeGreaterThan(0,
                "mv_activity_history_pattern must still have been refreshed even though mv_planner_task_pattern's refresh failed in the same job run");
        }
        finally
        {
            await RestoreMissingViewsAsync(CancellationToken);
        }
    }

    // ---- REFRESH CONCURRENTLY requires a unique index --------------------------------------------------

    [Theory]
    [InlineData(SuggestionPatternView.PlannerTask)]
    [InlineData(SuggestionPatternView.ActivityHistory)]
    [InlineData(SuggestionPatternView.TemplateSuggestion)]
    public async Task View_HasAUniqueIndex(SuggestionPatternView view)
    {
        await using var db = CreateDbContext();
        var hasUniqueIndex = await db.Database.SqlQueryRaw<bool>(
                "SELECT EXISTS (SELECT 1 FROM pg_indexes WHERE schemaname = 'public' AND tablename = {0} AND indexdef ILIKE 'CREATE UNIQUE INDEX%') AS \"Value\"",
                ViewNames[view])
            .SingleAsync(CancellationToken);

        hasUniqueIndex.Should().BeTrue(
            $"REFRESH MATERIALIZED VIEW CONCURRENTLY fails outright on {ViewNames[view]} without a unique index, and the job would then fail every save touching this entity type");
    }

    // ---- every declared view is actually installed ------------------------------------------------------

    // Replaces the old embedded-resources-vs-copied-files parity check: there is now only one installation
    // path (AppDbContextFixture calls SuggestionPatternViewInstaller), so the drift that check guarded
    // against cannot happen. What can still drift is SuggestionPatternView / ViewNames against the scripts
    // in sqlScripts/ — a view enum member with no script, or a script the enum doesn't know about.
    [Fact]
    public async Task EveryDeclaredView_ExistsAfterInstallation()
    {
        var assembly = typeof(SuggestionPatternViewInstaller).Assembly;
        var resourcePrefix = $"{assembly.GetName().Name}.infrastructure.persistence.sqlScripts.";
        var scriptViewNames = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(resourcePrefix, StringComparison.Ordinal) && name.EndsWith(".sql", StringComparison.Ordinal))
            .Select(name => name[resourcePrefix.Length..^".sql".Length])
            .Order()
            .ToList();

        scriptViewNames.Should().BeEquivalentTo(ViewNames.Values.Order(),
            "SuggestionPatternView drives the refresh queue while the scripts in sqlScripts/ drive installation -- a member on only one side means either a REFRESH of a view that was never created (42P01) or a view nothing ever refreshes");

        await using var db = CreateDbContext();
        foreach (var viewName in scriptViewNames)
        {
            var exists = await db.Database
                .SqlQueryRaw<bool>("SELECT to_regclass({0}) IS NOT NULL AS \"Value\"", $"public.{viewName}")
                .SingleAsync(CancellationToken);

            exists.Should().BeTrue($"the fixture runs the real SuggestionPatternViewInstaller, so {viewName} must exist in the test schema");
        }
    }
}
