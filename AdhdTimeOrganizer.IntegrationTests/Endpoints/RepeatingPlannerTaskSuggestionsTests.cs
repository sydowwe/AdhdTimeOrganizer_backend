using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Planning.application.dto.response.suggestion;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.dto.dto;
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
            activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "suggest-two-occurrences", ct: TestContext.Current.CancellationToken);
            calendarTargetId = await PlanningTestSeedHelper.SeedCalendarAsync(db, target, ct: TestContext.Current.CancellationToken);
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
            activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "suggest-three-occurrences", ct: TestContext.Current.CancellationToken);
            calendarTargetId = await PlanningTestSeedHelper.SeedCalendarAsync(db, target, ct: TestContext.Current.CancellationToken);
            // Same hour on all three, so this pins only that the times are averaged at all; the case where the
            // hour differs is AverageCrossingAnHour_IsTheMeanTimeOfDay below.
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

    /// <summary>
    /// Both views averaged hours and minutes separately, <c>make_time(AVG(hour)::int, AVG(minute)::int, 0)</c>,
    /// and a numeric cast to <c>int</c> rounds — so 09:50 / 10:10 / 10:00 came out as hour 9.67 → 10 and minute
    /// 20, i.e. 10:20, for three sittings whose mean is 10:00. The planner view feeds the activity A suggestion
    /// and the history view feeds B's; two activities because tier 2 suppresses tier 3 for the same one.
    /// </summary>
    [Fact(DisplayName = "Times either side of an hour average to the mean time of day, in both pattern views")]
    public async Task AverageCrossingAnHour_IsTheMeanTimeOfDay()
    {
        var target = new DateOnly(2027, 7, 5);
        long plannedActivityId, historyActivityId, calendarTargetId;
        await using (var db = CreateDbContext())
        {
            plannedActivityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "suggest-mean-planned", ct: CancellationToken);
            historyActivityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "suggest-mean-history", ct: CancellationToken);
            calendarTargetId = await PlanningTestSeedHelper.SeedCalendarAsync(db, target, ct: CancellationToken);

            (TimeOnly Start, TimeOnly End)[] sittings = [(new(9, 50), new(10, 50)), (new(10, 10), new(11, 10)), (new(10, 0), new(11, 0))];
            for (var i = 0; i < sittings.Length; i++)
            {
                var (start, end) = sittings[i];
                await SeedSameWeekdayTaskAsync(db, plannedActivityId, target, weeksAgo: i + 1, start, end);
                var date = target.AddDays(-7 * (i + 1));
                await SeedActivityHistoryAsync(db, historyActivityId, date.ToDateTime(start, DateTimeKind.Utc), date.ToDateTime(end, DateTimeKind.Utc));
            }
        }

        await RefreshSuggestionViewsAsync();

        var suggestions = await GetSuggestionsAsync(calendarTargetId);
        foreach (var (activityId, source) in new[] { (plannedActivityId, SuggestionSourceType.PlannedPattern), (historyActivityId, SuggestionSourceType.HistoryPattern) })
        {
            var pattern = suggestions.Should().ContainSingle(s => s.Activity.Id == activityId).Subject;
            pattern.SourceType.Should().Be(source);
            AsTime(pattern.StartTime).Should().Be(new TimeOnly(10, 0), $"{source}: the mean of 09:50, 10:10 and 10:00 is 10:00");
            AsTime(pattern.EndTime).Should().Be(new TimeOnly(11, 0), $"{source}: the mean of 10:50, 11:10 and 11:00 is 11:00");
        }
    }

    /// <summary>
    /// The mean is rounded to the minute, and a mean in the last half-minute of the day must not round up to
    /// 24:00 and wrap to 00:00 — that would offer a late-evening habit at the start of the day.
    /// </summary>
    [Fact(DisplayName = "A mean in the day's last half-minute stays at 23:59 rather than wrapping to 00:00")]
    public async Task MeanJustBeforeMidnight_ClampsTo2359()
    {
        var target = new DateOnly(2027, 7, 12);
        long activityId, calendarTargetId;
        await using (var db = CreateDbContext())
        {
            activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "suggest-mean-midnight", ct: CancellationToken);
            calendarTargetId = await PlanningTestSeedHelper.SeedCalendarAsync(db, target, ct: CancellationToken);
            // 23:59:40 / :50 / :55 — mean 23:59:48, which rounds to the minute as 24:00.
            int[] seconds = [40, 50, 55];
            for (var i = 0; i < seconds.Length; i++)
            {
                var start = target.AddDays(-7 * (i + 1)).ToDateTime(new TimeOnly(23, 59, seconds[i]), DateTimeKind.Utc);
                await SeedActivityHistoryAsync(db, activityId, start, start.AddMinutes(30));
            }
        }

        await RefreshSuggestionViewsAsync();

        var pattern = (await GetSuggestionsAsync(calendarTargetId)).Should().ContainSingle(s => s.Activity.Id == activityId).Subject;
        AsTime(pattern.StartTime).Should().Be(new TimeOnly(23, 59));
    }

    /// <summary>
    /// <c>activity_history</c> stores instants, and the view used to take their weekday and time of day in the
    /// <b>database session's</b> zone — UTC on every server this runs on, since Npgsql sets none — while the
    /// planner it feeds is in the user's wall-clock time. A Bratislava user who starts something at 00:30 every
    /// Tuesday (Monday 22:30 UTC in summer) was offered it on <b>Mondays at 22:30</b>. The view now converts
    /// through <c>user.timezone</c>.
    /// </summary>
    [Fact(DisplayName = "History patterns use the user's zone for both the weekday and the time of day")]
    public async Task HistoryPattern_IsInTheUsersZone()
    {
        var target = new DateOnly(2027, 6, 22); // a Tuesday, in CEST (UTC+2)
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Bratislava");
        long activityId, calendarTargetId, mondayCalendarId;
        await using (var db = CreateDbContext())
        {
            // Respawn re-creates the fixture user on UTC before the next test, so this does not leak.
            var user = await db.Set<User>().IgnoreQueryFilters().SingleAsync(u => u.Id == PlanningTestSeedHelper.TestUserId, CancellationToken);
            user.Timezone = zone;
            await db.SaveChangesAsync(CancellationToken);

            activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "suggest-user-zone", ct: CancellationToken);
            calendarTargetId = await PlanningTestSeedHelper.SeedCalendarAsync(db, target, ct: CancellationToken);
            mondayCalendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, target.AddDays(-1), ct: CancellationToken);
            for (var weeksAgo = 1; weeksAgo <= 3; weeksAgo++)
            {
                var localStart = target.AddDays(-7 * weeksAgo).ToDateTime(new TimeOnly(0, 30));
                var start = TimeZoneInfo.ConvertTimeToUtc(localStart, zone);
                await SeedActivityHistoryAsync(db, activityId, start, start.AddHours(1));
            }
        }

        await RefreshSuggestionViewsAsync();

        var pattern = (await GetSuggestionsAsync(calendarTargetId)).Should().ContainSingle(s => s.Activity.Id == activityId,
            "every sitting started on a Tuesday in the user's zone").Subject;
        pattern.ScheduledDays.Should().Equal("Tuesday");
        AsTime(pattern.StartTime).Should().Be(new TimeOnly(0, 30));
        AsTime(pattern.EndTime).Should().Be(new TimeOnly(1, 30));

        (await GetSuggestionsAsync(mondayCalendarId)).Should().NotContain(s => s.Activity.Id == activityId,
            "Monday is only the UTC weekday of those sittings");
    }

    [Fact(DisplayName = "3 occurrences including a cancelled one still fall short of a pattern")]
    public async Task ThreeOccurrencesWithOneCancelled_NoPattern()
    {
        var target = new DateOnly(2027, 5, 17);
        long activityId;
        long calendarTargetId;
        await using (var db = CreateDbContext())
        {
            activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "suggest-cancelled-excluded", ct: TestContext.Current.CancellationToken);
            calendarTargetId = await PlanningTestSeedHelper.SeedCalendarAsync(db, target, ct: TestContext.Current.CancellationToken);
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
            activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "suggest-tier1-suppresses-tier2", ct: TestContext.Current.CancellationToken);
            calendarTargetId = await PlanningTestSeedHelper.SeedCalendarAsync(db, target, ct: TestContext.Current.CancellationToken);
            await SeedSameWeekdayTaskAsync(db, activityId, target, weeksAgo: 1);
            await SeedSameWeekdayTaskAsync(db, activityId, target, weeksAgo: 2);
            await SeedSameWeekdayTaskAsync(db, activityId, target, weeksAgo: 3);
            await PlanningTestSeedHelper.SeedRepeatingPlannerTaskAsync(
                db, activityId, recurrenceType: RecurrenceType.DayOfWeek,
                start: new TimeOnly(9, 0), end: new TimeOnly(10, 0), ct: TestContext.Current.CancellationToken);
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
            activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "suggest-tier2-suppresses-tier3", ct: TestContext.Current.CancellationToken);
            calendarTargetId = await PlanningTestSeedHelper.SeedCalendarAsync(db, target, ct: TestContext.Current.CancellationToken);

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

    /// <summary>
    /// An activity that already has a <c>PlannerTask</c> on the day is not suggested for it, from any tier —
    /// the planner's <b>+</b> on such a suggestion created a second task for the same activity. The user-set
    /// row's task is <c>NotStarted</c> and the pattern row's is <c>Cancelled</c>, pinning that every status
    /// counts. A third activity with the same pattern and nothing planned is the control: a guard that
    /// dropped every suggestion would otherwise pass.
    /// </summary>
    [Fact(DisplayName = "An activity already planned on the day is not suggested for it, from any tier")]
    public async Task ActivityAlreadyPlannedOnTheDay_IsNotSuggested()
    {
        var target = new DateOnly(2027, 6, 21);
        long userSetActivityId, patternActivityId, controlActivityId, calendarTargetId;
        await using (var db = CreateDbContext())
        {
            userSetActivityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "suggest-planned-user-set", ct: TestContext.Current.CancellationToken);
            patternActivityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "suggest-planned-pattern", ct: TestContext.Current.CancellationToken);
            controlActivityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "suggest-planned-control", ct: TestContext.Current.CancellationToken);
            calendarTargetId = await PlanningTestSeedHelper.SeedCalendarAsync(db, target, ct: TestContext.Current.CancellationToken);

            var repeatingId = await PlanningTestSeedHelper.SeedRepeatingPlannerTaskAsync(
                db, userSetActivityId, recurrenceType: RecurrenceType.DayOfWeek, ct: TestContext.Current.CancellationToken);
            var repeating = await db.Set<AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning.RepeatingPlannerTask>()
                .SingleAsync(t => t.Id == repeatingId, CancellationToken);
            repeating.ScheduledDays.Add(target.DayOfWeek.ToString());
            await db.SaveChangesAsync(CancellationToken);

            foreach (var activityId in new[] { patternActivityId, controlActivityId })
            {
                for (var weeksAgo = 1; weeksAgo <= 3; weeksAgo++)
                    await SeedSameWeekdayTaskAsync(db, activityId, target, weeksAgo);
            }

            await PlanningTestSeedHelper.SeedPlannerTaskAsync(
                db, userSetActivityId, calendarTargetId, new TimeOnly(9, 0), new TimeOnly(10, 0), ct: TestContext.Current.CancellationToken);
            await PlanningTestSeedHelper.SeedPlannerTaskAsync(
                db, patternActivityId, calendarTargetId, new TimeOnly(11, 0), new TimeOnly(12, 0),
                status: PlannerTaskStatus.Cancelled, ct: TestContext.Current.CancellationToken);
        }

        await RefreshSuggestionViewsAsync();

        var suggestions = await GetSuggestionsAsync(calendarTargetId);
        suggestions.Should().NotContain(s => s.Activity.Id == userSetActivityId, "the user-set suggestion's activity is already on the day");
        suggestions.Should().NotContain(s => s.Activity.Id == patternActivityId, "the pattern's activity is already on the day, cancelled or not");
        suggestions.Should().ContainSingle(s => s.Activity.Id == controlActivityId)
            .Which.SourceType.Should().Be(SuggestionSourceType.PlannedPattern);
    }

    /// <summary>
    /// TEST-13 / <c>CQ-13</c>. A materialized view lags its base tables by design, so a row can name an
    /// activity that is already gone — and deleting an activity <b>cascades its planner tasks away</b>
    /// (<c>PlannerDeleteBehaviorTests</c>), so this is the case where the view row outlives both. Both tiers
    /// dereference <c>p.Activity.Role.Color</c>; before the guard that was an unhandled
    /// <c>NullReferenceException</c>, i.e. a 500 on a page load, from an action the user had just taken.
    /// <para>
    /// A second activity with the same pattern is seeded as the control: without it a guard that dropped every
    /// pattern row would pass this test.
    /// </para>
    /// </summary>
    [Fact(DisplayName = "CQ-13: a pattern row whose activity was just deleted is skipped, not a 500")]
    public async Task DeletedActivity_IsSkippedUntilTheViewCatchesUp()
    {
        var target = new DateOnly(2027, 6, 7);
        long doomedActivityId, survivingActivityId, calendarTargetId;
        await using (var db = CreateDbContext())
        {
            doomedActivityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "suggest-doomed", ct: TestContext.Current.CancellationToken);
            survivingActivityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "suggest-surviving", ct: TestContext.Current.CancellationToken);
            calendarTargetId = await PlanningTestSeedHelper.SeedCalendarAsync(db, target, ct: TestContext.Current.CancellationToken);

            foreach (var activityId in new[] { doomedActivityId, survivingActivityId })
            {
                for (var weeksAgo = 1; weeksAgo <= 3; weeksAgo++)
                {
                    // Tier 2 and tier 3 for the same activity and weekday, so one delete exercises both views.
                    await SeedSameWeekdayTaskAsync(db, activityId, target, weeksAgo);
                    await SeedActivityHistoryOnWeekdayAsync(db, activityId, target, weeksAgo);
                }
            }
        }

        await RefreshSuggestionViewsAsync();
        (await GetSuggestionsAsync(calendarTargetId)).Should().HaveCount(2, "both activities have a pattern before the delete");

        var delete = await CreateClient().DeleteAsync($"api/activity/{doomedActivityId}", CancellationToken);
        delete.IsSuccessStatusCode.Should().BeTrue();

        // Deliberately not refreshed: this is the state every read is in between refresh-job runs.
        var suggestions = await GetSuggestionsAsync(calendarTargetId);
        suggestions.Should().ContainSingle().Which.Activity.Id.Should().Be(survivingActivityId,
            "the stale rows in both views are skipped and the day still gets its other suggestion");
    }

    /// <summary>
    /// B27 part 5. The handler loads the calendar with <c>FindAsync</c> and never compares its <c>UserId</c> with
    /// the caller's; what keeps another account's calendar out is the per-user global query filter on
    /// <c>Calendar</c>, which <c>FindAsync</c> applies. This pins that the route answers 404, not suggestions
    /// steered by someone else's date and day type.
    /// </summary>
    [Fact(DisplayName = "Another user's calendar id is a 404, not their day's suggestions")]
    public async Task ForeignCalendar_IsNotFound()
    {
        var foreignUserId = await ActivityMergeTestSupport.SecondUserAsync(
            Fixture, $"repeating-suggestions-foreign-{Guid.NewGuid():N}@test.com");

        long foreignCalendarId;
        await using (var db = CreateDbContext())
            foreignCalendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, new DateOnly(2027, 6, 14), foreignUserId, CancellationToken);

        var response = await CreateClient().GetAsync($"api/repeating-planner-task/suggestions/{foreignCalendarId}", CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
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
        // The DateTimeKind overload is load-bearing: ToDateTime alone yields Kind=Unspecified, and Npgsql
        // refuses to write anything but UTC to a `timestamp with time zone` column.
        await SeedActivityHistoryAsync(db, activityId,
            date.ToDateTime(new TimeOnly(9, 0), DateTimeKind.Utc), date.ToDateTime(new TimeOnly(10, 0), DateTimeKind.Utc));
    }

    private static async Task SeedActivityHistoryAsync(Microsoft.EntityFrameworkCore.DbContext db, long activityId, DateTime startUtc, DateTime endUtc)
    {
        var length = endUtc - startUtc;
        db.Set<ActivityHistory>().Add(new ActivityHistory
        {
            UserId = PlanningTestSeedHelper.TestUserId,
            ActivityId = activityId,
            StartTimestamp = startUtc,
            EndTimestamp = endUtc,
            Length = new IntTime((int)length.TotalHours, length.Minutes)
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

    private static TimeOnly AsTime(TimeDto time) => new(time.Hours, time.Minutes);

    private async Task<List<SuggestionResponse>> GetSuggestionsAsync(long calendarId)
    {
        var response = await CreateClient().GetAsync($"api/repeating-planner-task/suggestions/{calendarId}", CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<List<SuggestionResponse>>(JsonOpts, CancellationToken))!;
    }
}
