using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Contracts.reminders;
using Sydowwe.Framework.Testing;
using Sydowwe.Reminders.domain.entity;
using Sydowwe.Reminders.domain.@enum;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Reminders;

/// <summary>
/// The overview's failure card, asserted on rows rather than on the route.
/// <para>
/// Three things here fail silently if they drift. <b>"Needs attention" is a predicate written twice</b> — once
/// for the card's count/sample and once for the dispatch history's <c>EffectiveFailuresOnly</c> filter — and if
/// the two ever diverge, the drill-down quietly lands on a different set of rows than the number the admin
/// clicked; nothing throws. <b>The two window lengths</b> are server constants echoed onto the wire purely so a
/// client can stop mirroring them, so a response that simply stops carrying them still deserialises fine and
/// leaves the UI confidently mislabelling its counts. And <b>the failure sample is capped</b>, which means a
/// client can only render "N of M" honestly if the count keeps reporting the true total while the array is
/// short — a bug that only shows up once there are more failures than the cap.
/// </para>
/// <para>
/// Dispatch rows are seeded directly rather than driven through the scan handler: the point of assertion is the
/// read-side predicate over an append-only ledger, and a reversed failure — the case the whole "effective"
/// qualifier exists for — cannot be produced by the scanner at all today.
/// </para>
/// </summary>
[Collection("Postgres")]
public class ReminderOverviewFailuresTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const string OwnerModule = "OverviewTest";

    [Fact(DisplayName = "The overview returns the rows behind the failure count — newest first, and never a reversed one")]
    public async Task Overview_ReturnsEffectiveFailureRows()
    {
        var now = DateTimeOffset.UtcNow;
        long olderFailureId;
        long newerFailureId;
        long reversedFailureId;

        await using (var seed = CreateDbContext())
        {
            var definition = await SeedDefinitionAsync(seed, "resolver-gone");

            olderFailureId = (await SeedFailureAsync(seed, definition, now.AddHours(-2),
                "No IReminderRecipientResolver registered for key 'gone'.")).Id;
            newerFailureId = (await SeedFailureAsync(seed, definition, now.AddHours(-1),
                "Dispatch threw InvalidOperationException.")).Id;

            // A failure that was already corrected — a reversal row points at it, so it is settled and must
            // appear in neither the count nor the sample.
            reversedFailureId = (await SeedFailureAsync(seed, definition, now.AddMinutes(-30), "Dispatch threw TimeoutException.")).Id;
            await SeedReversalAsync(seed, definition, reversedFailureId, now.AddMinutes(-5));
        }

        var overview = await ReadOverviewAsync();

        var failures = overview.GetProperty("failures").EnumerateArray().ToList();
        var mine = failures.Where(f => f.GetProperty("ownerModule").GetString() == OwnerModule).ToList();

        mine.Select(f => f.GetProperty("id").GetInt64()).Should().Equal([newerFailureId, olderFailureId],
            "the card renders the sample top-down and assumes most-recent-first; the reversed row is settled and belongs to neither list");
        mine.Select(f => f.GetProperty("id").GetInt64()).Should().NotContain(reversedFailureId);

        var newest = mine[0];
        newest.GetProperty("kind").GetString().Should().Be("resolver-gone");
        newest.GetProperty("detail").GetString().Should().Be("Dispatch threw InvalidOperationException.",
            "the error line is the whole reason the card beats a bare count");
        newest.GetProperty("occurrenceInstant").GetDateTimeOffset()
            .Should().NotBe(newest.GetProperty("dispatchedAt").GetDateTimeOffset(),
                "which occurrence failed and when the attempt ran are different questions, and the fixture sets them apart on purpose");
    }

    [Fact(DisplayName = "The overview states both windows it counted over instead of leaving the client to mirror them")]
    public async Task Overview_ReturnsBothWindowLengths()
    {
        var overview = await ReadOverviewAsync();

        overview.GetProperty("upcomingWindowDays").GetInt32().Should().Be(7,
            "the client labels the due-soon card from this; a window that changed without the field would mislabel silently");
        overview.GetProperty("recentWindowDays").GetInt32().Should().Be(7,
            "unlike UpcomingNext7Days, the recent window's length is not pinned by a field name — this is its only guard");
    }

    [Fact(DisplayName = "The dispatch history's EffectiveFailuresOnly filter selects exactly the rows the overview counted")]
    public async Task DispatchHistory_EffectiveFailuresOnly_MatchesTheOverviewCount()
    {
        var now = DateTimeOffset.UtcNow;
        long reversedFailureId;

        await using (var seed = CreateDbContext())
        {
            var definition = await SeedDefinitionAsync(seed, "drilldown");
            await SeedFailureAsync(seed, definition, now.AddHours(-3), "Dispatch threw HttpRequestException.");

            reversedFailureId = (await SeedFailureAsync(seed, definition, now.AddHours(-2), "Dispatch threw TimeoutException.")).Id;
            await SeedReversalAsync(seed, definition, reversedFailureId, now.AddMinutes(-10));

            // A Skipped row never needs attention, and must not be dragged in by an over-broad filter.
            await SeedSkipAsync(seed, definition, now.AddHours(-1));
        }

        var count = (await ReadOverviewAsync()).GetProperty("failedNeedingAttention").GetInt32();

        var response = await CreateAdminRoleClient().PostAsJsonAsync("api/reminder-dashboard/dispatch-history", new
        {
            useFilter = true,
            filter = new { effectiveFailuresOnly = true },
            sortBy = Array.Empty<object>(),
            itemsPerPage = 500,
            page = 1
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var grid = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts, CancellationToken);
        var rowIds = grid.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetInt64()).ToArray();

        rowIds.Should().HaveCount(count,
            "the whole point of the filter is that the 'view all failures' link lands on the set the count counted");
        rowIds.Should().NotContain(reversedFailureId);
    }

    // ---- helpers -----------------------------------------------------------------------------------

    private async Task<JsonElement> ReadOverviewAsync()
    {
        var response = await CreateAdminRoleClient().GetAsync("api/reminder-dashboard/overview", CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts, CancellationToken);
    }

    private async Task<ReminderDefinition> SeedDefinitionAsync(DbContext db, string kind)
    {
        var definition = new ReminderDefinition
        {
            OwnerModule = OwnerModule,
            SubjectType = "Fixture",
            // The key is uniquely indexed and these tests share a database, so every run needs its own subject.
            SubjectId = Guid.NewGuid().ToString("N"),
            Kind = kind,
            TemplateKey = "overview.fixture",
            ScheduleType = ReminderScheduleType.OneShot,
            RecipientMode = RecipientMode.ExplicitUsers,
            Status = ReminderStatus.Active
        };
        db.Set<ReminderDefinition>().Add(definition);
        await db.SaveChangesAsync(CancellationToken);
        return definition;
    }

    private Task<ReminderDispatch> SeedFailureAsync(DbContext db, ReminderDefinition definition, DateTimeOffset dispatchedAt, string detail) =>
        SeedDispatchAsync(db, definition, dispatchedAt, DispatchOutcome.Failed, failureDetail: detail);

    private Task<ReminderDispatch> SeedSkipAsync(DbContext db, ReminderDefinition definition, DateTimeOffset dispatchedAt) =>
        SeedDispatchAsync(db, definition, dispatchedAt, DispatchOutcome.Skipped, skipReason: SkipReason.NoRecipients);

    private Task<ReminderDispatch> SeedReversalAsync(DbContext db, ReminderDefinition definition, long reversesDispatchId, DateTimeOffset dispatchedAt) =>
        SeedDispatchAsync(db, definition, dispatchedAt, DispatchOutcome.Reversed, reversesDispatchId: reversesDispatchId);

    private async Task<ReminderDispatch> SeedDispatchAsync(
        DbContext db, ReminderDefinition definition, DateTimeOffset dispatchedAt, DispatchOutcome outcome,
        string? failureDetail = null, SkipReason? skipReason = null, long? reversesDispatchId = null)
    {
        var dispatch = new ReminderDispatch
        {
            ReminderDefinitionId = definition.Id,
            // Deliberately not equal to DispatchedAt — the two are distinct fields on the card.
            OccurrenceAt = dispatchedAt.AddMinutes(-7),
            DispatchedAt = dispatchedAt,
            Outcome = outcome,
            SkipReason = skipReason,
            FailureDetail = failureDetail,
            TemplateKeySnapshot = definition.TemplateKey,
            CorrelationId = "overview-failure-test",
            ReversesDispatchId = reversesDispatchId
        };
        db.Set<ReminderDispatch>().Add(dispatch);
        await db.SaveChangesAsync(CancellationToken);
        return dispatch;
    }
}
