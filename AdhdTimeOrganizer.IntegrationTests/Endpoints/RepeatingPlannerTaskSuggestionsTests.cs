using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Planning.application.dto.response.suggestion;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Sydowwe.Framework.domain.valueObject;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// TEST-3 / Section D — <c>GetSuggestionsRepeatingPlannerTaskEndpoint</c>'s three suggestion tiers, deduplicated
/// in order: user-set <c>RepeatingPlannerTask</c> rows, then <c>mv_planner_task_pattern</c>, then
/// <c>mv_activity_history_pattern</c>.
/// <para>
/// The two materialized views are refreshed asynchronously in production —
/// <c>SuggestionPatternRefreshInterceptor</c> only marks them dirty on save, and the actual
/// <c>REFRESH MATERIALIZED VIEW CONCURRENTLY</c> runs off the request path in
/// <c>SuggestionPatternRefreshJobHandler</c> (see PERF-1/PERF-2 in <c>review/portal/02-findings.md</c>). That
/// interceptor, however, is wired only into the real host's <c>AppDbContext</c> pipeline — the bare
/// <c>DbContext</c> this test seeds through never touches it, so driving the job handler here would drain an
/// empty dirty set and refresh nothing. <see cref="RefreshSuggestionViewsAsync"/> issues the
/// <c>REFRESH MATERIALIZED VIEW CONCURRENTLY</c> directly instead, sidestepping the queue for this file's
/// purposes; the queue/job wiring itself belongs to <c>Infrastructure/SuggestionPatternRefreshTests.cs</c>
/// (TEST-14), not here.
/// </para>
/// </summary>
[Collection("Postgres")]
public class RepeatingPlannerTaskSuggestionsTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    [Fact(DisplayName = "Exactly 2 occurrences on the same weekday produce no pattern suggestion")]
    public async Task TwoOccurrences_NoPattern()
    {
        var target = new DateOnly(2027, 5, 3); // any fixed date; only its DayOfWeek matters
        long activityId;
        long calendarTargetId;
        await using (var db = CreateDbContext())
        {
            activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "suggest-two-occurrences");
            calendarTargetId = await PlanningTestSeedHelper.SeedCalendarAsync(db, target);
            await SeedSameWeekdayTaskAsync(db, activityId, target, weeksAgo: 1);
            await SeedSameWeekdayTaskAsync(db, activityId, target, weeksAgo: 2);
        }

        await RefreshSuggestionViewsAsync();

        var suggestions = await GetSuggestionsAsync(calendarTargetId);
        suggestions.Should().NotContain(s => s.SourceType == SuggestionSourceType.PlannedPattern,
            "the view requires >= 3 occurrences of the same (activity, weekday)");
    }

    [Fact(DisplayName = "A 3rd occurrence makes the pattern appear, with averaged start/end times")]
    public async Task ThreeOccurrences_PatternAppears_WithAveragedTimes()
    {
        var target = new DateOnly(2027, 5, 10);
        long activityId;
        long calendarTargetId;
        await using (var db = CreateDbContext())
        {
            activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "suggest-three-occurrences");
            calendarTargetId = await PlanningTestSeedHelper.SeedCalendarAsync(db, target);
            // mv_planner_task_pattern averages hours and minutes SEPARATELY (AVG(EXTRACT(HOUR)), AVG(EXTRACT
            // (MINUTE)), each truncated to int), not as one combined elapsed-time average -- same hour across
            // all three occurrences isolates the minute-averaging behaviour cleanly: (0+10+20)/3 = 10.
            await SeedSameWeekdayTaskAsync(db, activityId, target, weeksAgo: 1, start: new TimeOnly(9, 0), end: new TimeOnly(10, 0));
            await SeedSameWeekdayTaskAsync(db, activityId, target, weeksAgo: 2, start: new TimeOnly(9, 10), end: new TimeOnly(10, 10));
            await SeedSameWeekdayTaskAsync(db, activityId, target, weeksAgo: 3, start: new TimeOnly(9, 20), end: new TimeOnly(10, 20));
        }

        await RefreshSuggestionViewsAsync();

        var suggestions = await GetSuggestionsAsync(calendarTargetId);
        var pattern = suggestions.Should().ContainSingle(s => s.SourceType == SuggestionSourceType.PlannedPattern && s.Activity.Id == activityId)
            .Subject;

        pattern.OccurrenceCount.Should().Be(3);
        pattern.StartTime.Hours.Should().Be(9);
        pattern.StartTime.Minutes.Should().Be(10);
        pattern.EndTime.Hours.Should().Be(10);
        pattern.EndTime.Minutes.Should().Be(10);
    }

    [Fact(DisplayName = "3 occurrences including a cancelled one still fall short of a pattern")]
    public async Task ThreeOccurrencesWithOneCancelled_NoPattern()
    {
        var target = new DateOnly(2027, 5, 17);
        long activityId;
        long calendarTargetId;
        await using (var db = CreateDbContext())
        {
            activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "suggest-cancelled-excluded");
            calendarTargetId = await PlanningTestSeedHelper.SeedCalendarAsync(db, target);
            await SeedSameWeekdayTaskAsync(db, activityId, target, weeksAgo: 1);
            await SeedSameWeekdayTaskAsync(db, activityId, target, weeksAgo: 2);
            await SeedSameWeekdayTaskAsync(db, activityId, target, weeksAgo: 3, status: PlannerTaskStatus.Cancelled);
        }

        await RefreshSuggestionViewsAsync();

        var suggestions = await GetSuggestionsAsync(calendarTargetId);
        suggestions.Should().NotContain(s => s.SourceType == SuggestionSourceType.PlannedPattern && s.Activity.Id == activityId,
            "mv_planner_task_pattern filters out status = Cancelled (4) before counting occurrences");
    }

    [Fact(DisplayName = "A user-set RepeatingPlannerTask suppresses the PlannedPattern suggestion for the same activity")]
    public async Task Tier1Suppresses_Tier2()
    {
        var target = new DateOnly(2027, 5, 24);
        long activityId;
        long calendarTargetId;
        await using (var db = CreateDbContext())
        {
            activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "suggest-tier1-suppresses-tier2");
            calendarTargetId = await PlanningTestSeedHelper.SeedCalendarAsync(db, target);
            await SeedSameWeekdayTaskAsync(db, activityId, target, weeksAgo: 1);
            await SeedSameWeekdayTaskAsync(db, activityId, target, weeksAgo: 2);
            await SeedSameWeekdayTaskAsync(db, activityId, target, weeksAgo: 3);
            await PlanningTestSeedHelper.SeedRepeatingPlannerTaskAsync(
                db, activityId, recurrenceType: RecurrenceType.DayOfWeek,
                start: new TimeOnly(9, 0), end: new TimeOnly(10, 0));
        }

        await RefreshSuggestionViewsAsync();
        await using (var db = CreateDbContext())
        {
            var scheduled = target.DayOfWeek.ToString();
            var task = await db.Set<AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning.RepeatingPlannerTask>()
                .SingleAsync(t => t.ActivityId == activityId, CancellationToken);
            task.ScheduledDays.Add(scheduled);
            await db.SaveChangesAsync(CancellationToken);
        }

        var suggestions = await GetSuggestionsAsync(calendarTargetId);
        suggestions.Where(s => s.Activity.Id == activityId).Should().ContainSingle()
            .Which.SourceType.Should().Be(SuggestionSourceType.UserSet, "tier 1 wins and tier 2 is suppressed for the same activity");
    }

    [Fact(DisplayName = "A PlannedPattern suggestion suppresses the matching HistoryPattern suggestion")]
    public async Task Tier2Suppresses_Tier3()
    {
        var target = new DateOnly(2027, 5, 31);
        long activityId;
        long calendarTargetId;
        await using (var db = CreateDbContext())
        {
            activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "suggest-tier2-suppresses-tier3");
            calendarTargetId = await PlanningTestSeedHelper.SeedCalendarAsync(db, target);

            // Tier 2: 3 PlannerTask occurrences on the target's weekday.
            await SeedSameWeekdayTaskAsync(db, activityId, target, weeksAgo: 1);
            await SeedSameWeekdayTaskAsync(db, activityId, target, weeksAgo: 2);
            await SeedSameWeekdayTaskAsync(db, activityId, target, weeksAgo: 3);

            // Tier 3: 3 ActivityHistory occurrences for the SAME activity and the SAME weekday/pattern value.
            await SeedActivityHistoryOnWeekdayAsync(db, activityId, target, weeksAgo: 1);
            await SeedActivityHistoryOnWeekdayAsync(db, activityId, target, weeksAgo: 2);
            await SeedActivityHistoryOnWeekdayAsync(db, activityId, target, weeksAgo: 3);
        }

        await RefreshSuggestionViewsAsync();

        var suggestions = await GetSuggestionsAsync(calendarTargetId);
        var forActivity = suggestions.Where(s => s.Activity.Id == activityId).ToList();
        forActivity.Should().ContainSingle().Which.SourceType.Should().Be(SuggestionSourceType.PlannedPattern,
            "tier 2 already covers (activity, pattern type, pattern value), so tier 3's matching row is suppressed");
    }

    // ─── helpers ─────────────────────────────────────────────────────────────

    private async Task SeedSameWeekdayTaskAsync(
        Microsoft.EntityFrameworkCore.DbContext db, long activityId, DateOnly target, int weeksAgo,
        TimeOnly? start = null, TimeOnly? end = null, PlannerTaskStatus status = PlannerTaskStatus.NotStarted)
    {
        var date = target.AddDays(-7 * weeksAgo);
        var calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, date);
        await PlanningTestSeedHelper.SeedPlannerTaskAsync(
            db, activityId, calendarId, start ?? new TimeOnly(9, 0), end ?? new TimeOnly(10, 0), status: status);
    }

    private static async Task SeedActivityHistoryOnWeekdayAsync(Microsoft.EntityFrameworkCore.DbContext db, long activityId, DateOnly target, int weeksAgo)
    {
        var date = target.AddDays(-7 * weeksAgo);
        var start = date.ToDateTime(new TimeOnly(9, 0));
        var end = date.ToDateTime(new TimeOnly(10, 0));
        db.Set<ActivityHistory>().Add(new ActivityHistory
        {
            UserId = PlanningTestSeedHelper.TestUserId,
            ActivityId = activityId,
            StartTimestamp = start,
            EndTimestamp = end,
            Length = new IntTime(1, 0)
        });
        await db.SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>
    /// <c>SuggestionPatternRefreshJobHandler</c> only refreshes views that <c>SuggestionPatternRefreshInterceptor</c>
    /// marked dirty in <c>ISuggestionPatternRefreshQueue</c> — and that interceptor is wired only on the real
    /// host's <c>AppDbContext</c> pipeline (via DI), not on the bare fixture <c>DbContext</c> that
    /// <see cref="PostgresTestBase.CreateDbContext"/> hands back for seeding. Seeding here never goes through
    /// an HTTP request, so nothing ever marks the queue dirty and driving the job handler directly (as first
    /// attempted) is a no-op — it drains an empty set. Issuing the <c>REFRESH MATERIALIZED VIEW CONCURRENTLY</c>
    /// directly sidesteps that gap; the queue/job wiring itself has its own coverage in
    /// <c>Infrastructure/SuggestionPatternRefreshTests.cs</c> (TEST-14), which is not this file's concern.
    /// </summary>
    private async Task RefreshSuggestionViewsAsync()
    {
        await using var db = CreateDbContext();
#pragma warning disable EF1002
        // Schema-qualified: the views belong to the Planning slice and are created in its schema.
        await db.Database.ExecuteSqlRawAsync($"REFRESH MATERIALIZED VIEW CONCURRENTLY {ModuleSchemas.Planning}.mv_planner_task_pattern", CancellationToken);
        await db.Database.ExecuteSqlRawAsync($"REFRESH MATERIALIZED VIEW CONCURRENTLY {ModuleSchemas.Planning}.mv_activity_history_pattern", CancellationToken);
#pragma warning restore EF1002
    }

    private async Task<List<SuggestionResponse>> GetSuggestionsAsync(long calendarId)
    {
        var response = await CreateClient().GetAsync($"api/repeating-planner-task/suggestions/{calendarId}", CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<List<SuggestionResponse>>(JsonOpts, CancellationToken))!;
    }
}
