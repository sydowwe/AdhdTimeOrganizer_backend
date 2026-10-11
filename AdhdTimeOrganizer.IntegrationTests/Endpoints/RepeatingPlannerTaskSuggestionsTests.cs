using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.History.domain.model.@enum;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Planning.application.dto.response.suggestion;
using AdhdTimeOrganizer.Planning.domain.model.entity.suggestion;
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
        var target = UpcomingMonday();
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
        var target = UpcomingMonday();
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
        var target = UpcomingMonday();
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
    /// Time of day is circular. Averaged as plain seconds since midnight, 23:50 / 00:10 / 00:00 came out as
    /// 08:00 — a habit that straddles midnight suggested for the morning. The views average them as angles on
    /// the clock instead, so the mean is midnight, as it is for anyone reading the times.
    /// </summary>
    [Fact(DisplayName = "Times either side of midnight average to midnight, in both pattern views")]
    public async Task AverageAcrossMidnight_IsMidnight()
    {
        var target = UpcomingMonday();
        long plannedActivityId, historyActivityId, calendarTargetId;
        await using (var db = CreateDbContext())
        {
            plannedActivityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "suggest-midnight-planned", ct: CancellationToken);
            historyActivityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "suggest-midnight-history", ct: CancellationToken);
            calendarTargetId = await PlanningTestSeedHelper.SeedCalendarAsync(db, target, ct: CancellationToken);

            TimeOnly[] starts = [new(23, 50), new(0, 10), new(0, 0)];
            for (var i = 0; i < starts.Length; i++)
            {
                var start = starts[i];
                var end = start.AddMinutes(30);
                await SeedSameWeekdayTaskAsync(db, plannedActivityId, target, weeksAgo: i + 1, start, end);
                var startUtc = target.AddDays(-7 * (i + 1)).ToDateTime(start, DateTimeKind.Utc);
                await SeedActivityHistoryAsync(db, historyActivityId, startUtc, startUtc.AddMinutes(30));
            }
        }

        await RefreshSuggestionViewsAsync();

        var suggestions = await GetSuggestionsAsync(calendarTargetId);
        foreach (var activityId in new[] { plannedActivityId, historyActivityId })
        {
            var pattern = suggestions.Should().ContainSingle(s => s.Activity.Id == activityId).Subject;
            AsTime(pattern.StartTime).Should().Be(new TimeOnly(0, 0), $"{pattern.SourceType}: the mean of 23:50, 00:10 and 00:00 is midnight");
            AsTime(pattern.EndTime).Should().Be(new TimeOnly(0, 30), $"{pattern.SourceType}: the mean of 00:20, 00:40 and 00:30 is 00:30");
        }
    }

    /// <summary>
    /// A Pomodoro writes one row per phase, all under one <c>ActivityHistorySession</c>. The history view counted
    /// rows, so a single four-period Pomodoro was already "three occurrences" and made a pattern on its own. It
    /// now counts sittings — a session is one, from its first focus period's start to its last focus period's
    /// end — and break rows take no part, whichever activity they were logged against.
    /// </summary>
    [Fact(DisplayName = "A Pomodoro is one sitting: counted once, ending at its last focus period, breaks ignored")]
    public async Task Pomodoro_IsOneSitting_EndingAtItsLastFocusPeriod()
    {
        var target = UpcomingMonday();
        long focusActivityId, restActivityId, calendarTargetId;
        await using (var db = CreateDbContext())
        {
            focusActivityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "suggest-pomodoro-focus", ct: CancellationToken);
            restActivityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "suggest-pomodoro-rest", ct: CancellationToken);
            calendarTargetId = await PlanningTestSeedHelper.SeedCalendarAsync(db, target, ct: CancellationToken);
            await SeedPomodoroAsync(db, focusActivityId, restActivityId, target.AddDays(-7));
            await SeedPomodoroAsync(db, focusActivityId, restActivityId, target.AddDays(-14));
        }

        await RefreshSuggestionViewsAsync();
        (await GetSuggestionsAsync(calendarTargetId)).Should().BeEmpty(
            "two Pomodoros are two sittings, short of the three a pattern needs, however many rows they wrote");

        await using (var db = CreateDbContext())
            await SeedPomodoroAsync(db, focusActivityId, restActivityId, target.AddDays(-21));
        await RefreshSuggestionViewsAsync();

        var suggestions = await GetSuggestionsAsync(calendarTargetId);
        suggestions.Should().NotContain(s => s.Activity.Id == restActivityId, "breaks are not sittings of the rest activity");
        var pattern = suggestions.Should().ContainSingle(s => s.Activity.Id == focusActivityId).Subject;
        pattern.OccurrenceCount.Should().Be(3);
        AsTime(pattern.StartTime).Should().Be(new TimeOnly(9, 0));
        AsTime(pattern.EndTime).Should().Be(new TimeOnly(10, 55), "the sitting ends with its last focus period, not the long break after it");
    }

    /// <summary>
    /// 00:00 / 08:00 / 16:00 are evenly spread round the clock, so their angles cancel and there is no circular
    /// mean — atan2 of the leftover floating-point noise would be an arbitrary time. The views fall back to the
    /// plain average, 08:00, which is at least stable.
    /// </summary>
    [Fact(DisplayName = "Times that cancel out on the clock fall back to the plain average")]
    public async Task TimesThatCancelOut_FallBackToThePlainAverage()
    {
        var target = UpcomingMonday();
        long activityId, calendarTargetId;
        await using (var db = CreateDbContext())
        {
            activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "suggest-mean-cancels", ct: CancellationToken);
            calendarTargetId = await PlanningTestSeedHelper.SeedCalendarAsync(db, target, ct: CancellationToken);
            TimeOnly[] starts = [new(0, 0), new(8, 0), new(16, 0)];
            for (var i = 0; i < starts.Length; i++)
                await SeedSameWeekdayTaskAsync(db, activityId, target, weeksAgo: i + 1, starts[i], starts[i].AddHours(1));
        }

        await RefreshSuggestionViewsAsync();

        var pattern = (await GetSuggestionsAsync(calendarTargetId)).Should().ContainSingle(s => s.Activity.Id == activityId).Subject;
        AsTime(pattern.StartTime).Should().Be(new TimeOnly(8, 0));
        AsTime(pattern.EndTime).Should().Be(new TimeOnly(9, 0));
    }

    /// <summary>
    /// The mean is rounded to the nearest minute <b>on the clock</b>, so one in the day's last half-minute is
    /// 00:00, not 24:00 (which <c>make_interval(...)::time</c> would reject) and not a clamped 23:59. A clamp
    /// read sensibly while the mean was linear, but against a circular one it makes a habit centred exactly
    /// on midnight land on 23:59 or 00:00 depending on floating-point noise either side of zero.
    /// </summary>
    [Fact(DisplayName = "A mean in the day's last half-minute rounds to 00:00, as the clock does")]
    public async Task MeanJustBeforeMidnight_RoundsToMidnight()
    {
        var target = UpcomingMonday();
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
        AsTime(pattern.StartTime).Should().Be(new TimeOnly(0, 0), "23:59:48 is twelve seconds from midnight");
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
        // A Tuesday. Bratislava is UTC+1 or +2 by season; either way 00:30 local is still Monday in UTC.
        var target = UpcomingMonday().AddDays(1);
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
        var target = UpcomingMonday();
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
        var target = UpcomingMonday();
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
        var target = UpcomingMonday();
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
    /// FO1. On an away day only the recurrences the user set apply: the two pattern tiers are weekday statistics
    /// that workdays dominate, so a vacation Monday would otherwise be offered the office Monday. The same
    /// weekday a week later, a workday, is the control — a guard that dropped tier 2 everywhere would pass
    /// without it.
    /// </summary>
    [Fact(DisplayName = "FO1: an away day drops both pattern tiers and keeps the user-set recurrences")]
    public async Task AwayDay_KeepsOnlyUserSetSuggestions()
    {
        var target = UpcomingMonday();
        long patternActivityId, userSetActivityId, awayCalendarId, workdayCalendarId;
        await using (var db = CreateDbContext())
        {
            patternActivityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "fo1-pattern", ct: CancellationToken);
            userSetActivityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "fo1-user-set", ct: CancellationToken);
            awayCalendarId = await PlanningTestSeedHelper.SeedCalendarAsync(
                db, target, ct: CancellationToken, dayType: AdhdTimeOrganizer.Core.domain.model.@enum.DayType.Vacation);
            workdayCalendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, target.AddDays(7), ct: CancellationToken);

            await SeedSameWeekdayTaskAsync(db, patternActivityId, target, weeksAgo: 1);
            await SeedSameWeekdayTaskAsync(db, patternActivityId, target, weeksAgo: 2);
            await SeedSameWeekdayTaskAsync(db, patternActivityId, target, weeksAgo: 3);

            await PlanningTestSeedHelper.SeedRepeatingPlannerTaskAsync(
                db, userSetActivityId, recurrenceType: RecurrenceType.DayOfWeek,
                start: new TimeOnly(8, 0), end: new TimeOnly(8, 15), ct: CancellationToken);
            var task = await db.Set<AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning.RepeatingPlannerTask>()
                .SingleAsync(t => t.ActivityId == userSetActivityId, CancellationToken);
            task.ScheduledDays.Add(target.DayOfWeek.ToString());
            await db.SaveChangesAsync(CancellationToken);
        }

        await RefreshSuggestionViewsAsync();

        var away = await GetSuggestionsAsync(awayCalendarId);
        away.Should().ContainSingle().Which.Should().Match<SuggestionResponse>(s =>
            s.Activity.Id == userSetActivityId && s.SourceType == SuggestionSourceType.UserSet);

        var workday = await GetSuggestionsAsync(workdayCalendarId);
        workday.Select(s => (s.Activity.Id, s.SourceType)).Should().BeEquivalentTo(
            [(userSetActivityId, SuggestionSourceType.UserSet), (patternActivityId, SuggestionSourceType.PlannedPattern)]);
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
        var target = UpcomingMonday();
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
        var target = UpcomingMonday();
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
    /// OP10. A weekly rule may carry an active window, and the planner names it as the suggestion's reason
    /// ("every Mon · until …"). The endpoint checked the window only for DateRange rules, so a weekly rule kept
    /// being suggested after its end date. Three weekly rules on the same weekday: one whose window has ended,
    /// one not started yet, and an open-ended control that must still be suggested.
    /// </summary>
    [Fact(DisplayName = "A weekly rule is suggested only inside its active window")]
    public async Task WeeklyRule_OutsideItsWindow_IsNotSuggested()
    {
        var target = new DateOnly(2027, 8, 9);
        long endedActivityId, futureActivityId, openActivityId, calendarTargetId;
        await using (var db = CreateDbContext())
        {
            endedActivityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "suggest-window-ended", ct: CancellationToken);
            futureActivityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "suggest-window-future", ct: CancellationToken);
            openActivityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "suggest-window-open", ct: CancellationToken);
            calendarTargetId = await PlanningTestSeedHelper.SeedCalendarAsync(db, target, ct: CancellationToken);

            (long ActivityId, DateOnly? From, DateOnly? To)[] rules =
            [
                (endedActivityId, target.AddDays(-30), target.AddDays(-1)),
                (futureActivityId, target.AddDays(1), null),
                (openActivityId, null, target)
            ];
            foreach (var (activityId, from, to) in rules)
            {
                var id = await PlanningTestSeedHelper.SeedRepeatingPlannerTaskAsync(db, activityId, ct: CancellationToken);
                var rule = await db.Set<AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning.RepeatingPlannerTask>()
                    .SingleAsync(t => t.Id == id, CancellationToken);
                rule.ScheduledDays.Add(target.DayOfWeek.ToString());
                rule.ActiveFromDate = from;
                rule.ActiveToDate = to;
                await db.SaveChangesAsync(CancellationToken);
            }
        }

        var suggestions = await GetSuggestionsAsync(calendarTargetId);
        suggestions.Should().NotContain(s => s.Activity.Id == endedActivityId, "its window ended the day before");
        suggestions.Should().NotContain(s => s.Activity.Id == futureActivityId, "its window starts the day after");
        suggestions.Should().ContainSingle(s => s.Activity.Id == openActivityId, "the end date is inclusive and the start is open")
            .Which.ActiveToDate.Should().Be(target);
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

    // ─── FO11: recency window and dismissal ─────────────────────────────────────

    /// <summary>
    /// FO11. "Tuesday: Swimming 18:00" from a course dropped last spring. Both views counted sittings over all
    /// history, so three old ones made a pattern for ever. They now count the last 12 weeks only. Sixteen to
    /// fourteen weeks back is out of it whatever weekday today is; a planner pattern and a history pattern, one
    /// activity each, plus a recent pattern as the control.
    /// </summary>
    [Fact(DisplayName = "FO11: three sittings from before the twelve-week window make no weekday pattern, in either view")]
    public async Task SittingsOlderThanTheWindow_MakeNoPattern()
    {
        var target = UpcomingMonday();
        long stalePlannedId, staleHistoryId, recentId, calendarTargetId;
        await using (var db = CreateDbContext())
        {
            stalePlannedId = await PlanningTestSeedHelper.SeedActivityAsync(db, "fo11-stale-planned", ct: CancellationToken);
            staleHistoryId = await PlanningTestSeedHelper.SeedActivityAsync(db, "fo11-stale-history", ct: CancellationToken);
            recentId = await PlanningTestSeedHelper.SeedActivityAsync(db, "fo11-recent", ct: CancellationToken);
            calendarTargetId = await PlanningTestSeedHelper.SeedCalendarAsync(db, target, ct: CancellationToken);

            for (var weeksAgo = 14; weeksAgo <= 16; weeksAgo++)
            {
                await SeedSameWeekdayTaskAsync(db, stalePlannedId, target, weeksAgo);
                await SeedActivityHistoryOnWeekdayAsync(db, staleHistoryId, target, weeksAgo);
            }
            for (var weeksAgo = 1; weeksAgo <= 3; weeksAgo++)
                await SeedSameWeekdayTaskAsync(db, recentId, target, weeksAgo);
        }

        await RefreshSuggestionViewsAsync();

        var suggestions = await GetSuggestionsAsync(calendarTargetId);
        suggestions.Should().NotContain(s => s.Activity.Id == stalePlannedId, "its planner sittings are all older than 12 weeks");
        suggestions.Should().NotContain(s => s.Activity.Id == staleHistoryId, "its tracked sittings are all older than 12 weeks");
        suggestions.Should().ContainSingle(s => s.Activity.Id == recentId);
    }

    /// <summary>
    /// FO11. Enough sittings inside the window is not enough on its own: a habit dropped six weeks ago still has
    /// three in the last twelve. A weekday pattern also needs one in the last four weeks — three missed weeks and
    /// it stops being offered.
    /// </summary>
    [Fact(DisplayName = "FO11: a weekday pattern with no sitting in the last four weeks is not suggested, in either view")]
    public async Task PatternNotSeenForFourWeeks_IsNotSuggested()
    {
        var target = UpcomingMonday();
        long lapsedPlannedId, lapsedHistoryId, calendarTargetId;
        await using (var db = CreateDbContext())
        {
            lapsedPlannedId = await PlanningTestSeedHelper.SeedActivityAsync(db, "fo11-lapsed-planned", ct: CancellationToken);
            lapsedHistoryId = await PlanningTestSeedHelper.SeedActivityAsync(db, "fo11-lapsed-history", ct: CancellationToken);
            calendarTargetId = await PlanningTestSeedHelper.SeedCalendarAsync(db, target, ct: CancellationToken);

            // Six to eight weeks back from a target at most a week ahead: the latest is 35+ days ago.
            for (var weeksAgo = 6; weeksAgo <= 8; weeksAgo++)
            {
                await SeedSameWeekdayTaskAsync(db, lapsedPlannedId, target, weeksAgo);
                await SeedActivityHistoryOnWeekdayAsync(db, lapsedHistoryId, target, weeksAgo);
            }
        }

        await RefreshSuggestionViewsAsync();

        (await GetSuggestionsAsync(calendarTargetId)).Should().BeEmpty(
            "both activities have three sittings inside twelve weeks, but none inside the last four");
    }

    /// <summary>
    /// FO11, the monthly half: a day-of-month pattern counts twelve months (three a year is the floor) and needs
    /// one in the last three. Rent on the 5th, three months running, is offered; the same three a year ago is not.
    /// </summary>
    [Fact(DisplayName = "FO11: a day-of-month pattern counts the last year and needs a sitting in the last three months")]
    public async Task DayOfMonthPattern_UsesItsOwnWindow()
    {
        var target = UpcomingMonday();
        while (target.Day > 28)
            target = target.AddDays(7); // so every month back has the same day of the month
        long currentId, lapsedId, calendarTargetId;
        await using (var db = CreateDbContext())
        {
            currentId = await PlanningTestSeedHelper.SeedActivityAsync(db, "fo11-monthly-current", ct: CancellationToken);
            lapsedId = await PlanningTestSeedHelper.SeedActivityAsync(db, "fo11-monthly-lapsed", ct: CancellationToken);
            calendarTargetId = await PlanningTestSeedHelper.SeedCalendarAsync(db, target, ct: CancellationToken);

            for (var monthsAgo = 1; monthsAgo <= 3; monthsAgo++)
                await SeedTaskOnAsync(db, currentId, target.AddMonths(-monthsAgo));
            for (var monthsAgo = 5; monthsAgo <= 7; monthsAgo++)
                await SeedTaskOnAsync(db, lapsedId, target.AddMonths(-monthsAgo));
        }

        await RefreshSuggestionViewsAsync();

        var suggestions = await GetSuggestionsAsync(calendarTargetId);
        suggestions.Should().ContainSingle(s => s.Activity.Id == currentId && s.RecurrenceType == RecurrenceType.DayOfMonth)
            .Which.ScheduledDates.Should().Equal(target.Day);
        suggestions.Should().NotContain(s => s.Activity.Id == lapsedId && s.RecurrenceType == RecurrenceType.DayOfMonth,
            "its latest sitting is five months back, past the three-month last-seen limit");
    }

    /// <summary>
    /// FO11. "×, not any more". The dismissed activity has a planner pattern <b>and</b> a history pattern on the
    /// same weekday; tier 3 is normally suppressed only by tier 2, so a dismissal that hid tier 2 alone would let
    /// the history row take its place. A second pattern is the control.
    /// </summary>
    [Fact(DisplayName = "FO11: a dismissed pattern is hidden from both pattern tiers, and only that one")]
    public async Task DismissedPattern_IsHiddenFromBothTiers()
    {
        var target = UpcomingMonday();
        long dismissedId, controlId, calendarTargetId;
        await using (var db = CreateDbContext())
        {
            dismissedId = await PlanningTestSeedHelper.SeedActivityAsync(db, "fo11-dismissed", ct: CancellationToken);
            controlId = await PlanningTestSeedHelper.SeedActivityAsync(db, "fo11-dismiss-control", ct: CancellationToken);
            calendarTargetId = await PlanningTestSeedHelper.SeedCalendarAsync(db, target, ct: CancellationToken);
            for (var weeksAgo = 1; weeksAgo <= 3; weeksAgo++)
            {
                await SeedSameWeekdayTaskAsync(db, dismissedId, target, weeksAgo);
                await SeedActivityHistoryOnWeekdayAsync(db, dismissedId, target, weeksAgo);
                await SeedSameWeekdayTaskAsync(db, controlId, target, weeksAgo);
            }
        }

        await RefreshSuggestionViewsAsync();
        (await GetSuggestionsAsync(calendarTargetId)).Should().Contain(s => s.Activity.Id == dismissedId);

        var response = await DismissAsync(dismissedId, RecurrenceType.DayOfWeek, MondayIso);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var suggestions = await GetSuggestionsAsync(calendarTargetId);
        suggestions.Should().NotContain(s => s.Activity.Id == dismissedId, "neither the planner nor the history pattern may stand in");
        suggestions.Should().ContainSingle(s => s.Activity.Id == controlId);
    }

    /// <summary>
    /// FO11. A dismissal holds for one window length and then lapses — by then every sitting the view still
    /// counts was made after it, so a pattern that is back has re-formed. Two dismissals of two live patterns:
    /// one a day inside twelve weeks old, one a day past it.
    /// </summary>
    [Fact(DisplayName = "FO11: a weekday dismissal lapses after twelve weeks")]
    public async Task Dismissal_LapsesAfterTheWindow()
    {
        var target = UpcomingMonday();
        var today = Today();
        long holdingId, lapsedId, calendarTargetId;
        await using (var db = CreateDbContext())
        {
            holdingId = await PlanningTestSeedHelper.SeedActivityAsync(db, "fo11-dismissal-holds", ct: CancellationToken);
            lapsedId = await PlanningTestSeedHelper.SeedActivityAsync(db, "fo11-dismissal-lapsed", ct: CancellationToken);
            calendarTargetId = await PlanningTestSeedHelper.SeedCalendarAsync(db, target, ct: CancellationToken);
            for (var weeksAgo = 1; weeksAgo <= 3; weeksAgo++)
            {
                await SeedSameWeekdayTaskAsync(db, holdingId, target, weeksAgo);
                await SeedSameWeekdayTaskAsync(db, lapsedId, target, weeksAgo);
            }
            await SeedDismissalAsync(db, holdingId, RecurrenceType.DayOfWeek, MondayIso, today.AddDays(-83));
            await SeedDismissalAsync(db, lapsedId, RecurrenceType.DayOfWeek, MondayIso, today.AddDays(-84));
        }

        await RefreshSuggestionViewsAsync();

        var suggestions = await GetSuggestionsAsync(calendarTargetId);
        suggestions.Should().NotContain(s => s.Activity.Id == holdingId, "83 days is inside the 84-day window");
        suggestions.Should().ContainSingle(s => s.Activity.Id == lapsedId, "84 days is the whole window: the dismissal has lapsed");
    }

    /// <summary>
    /// FO11. The snackbar's Undo deletes the caller's dismissal and the pattern is back. Another user's dismissal
    /// of the same key (their own activity id, so a different row) is the control: the delete is a bulk
    /// <c>ExecuteDeleteAsync</c>, and only the global user filter keeps it on the caller's rows.
    /// </summary>
    [Fact(DisplayName = "FO11: undoing a dismissal brings the pattern back and touches only the caller's row")]
    public async Task UndoDismiss_BringsThePatternBack()
    {
        var target = UpcomingMonday();
        var foreignUserId = await ActivityMergeTestSupport.SecondUserAsync(
            Fixture, $"fo11-undo-foreign-{Guid.NewGuid():N}@test.com");
        long activityId, foreignActivityId, calendarTargetId;
        await using (var db = CreateDbContext())
        {
            activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "fo11-undo", ct: CancellationToken);
            foreignActivityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "fo11-undo-foreign", foreignUserId, CancellationToken);
            calendarTargetId = await PlanningTestSeedHelper.SeedCalendarAsync(db, target, ct: CancellationToken);
            for (var weeksAgo = 1; weeksAgo <= 3; weeksAgo++)
                await SeedSameWeekdayTaskAsync(db, activityId, target, weeksAgo);

            db.Set<PlannerSuggestionDismissal>().Add(new PlannerSuggestionDismissal
            {
                UserId = foreignUserId, ActivityId = foreignActivityId,
                PatternType = RecurrenceType.DayOfWeek, PatternValue = MondayIso, DismissedOn = Today()
            });
            await db.SaveChangesAsync(CancellationToken);
        }

        await RefreshSuggestionViewsAsync();
        (await DismissAsync(activityId, RecurrenceType.DayOfWeek, MondayIso)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetSuggestionsAsync(calendarTargetId)).Should().NotContain(s => s.Activity.Id == activityId);

        var undo = await CreateClient().PostAsJsonAsync("api/repeating-planner-task/suggestions/dismiss/undo",
            new { activityId, patternType = RecurrenceType.DayOfWeek, patternValue = MondayIso }, JsonOpts, CancellationToken);
        undo.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await GetSuggestionsAsync(calendarTargetId)).Should().ContainSingle(s => s.Activity.Id == activityId);

        await using var assertDb = CreateDbContext();
        var remaining = await assertDb.Set<PlannerSuggestionDismissal>().IgnoreQueryFilters()
            .Select(d => d.UserId).ToListAsync(CancellationToken);
        remaining.Should().Equal([foreignUserId], "the other user's dismissal is not the caller's to undo");
    }

    [Fact(DisplayName = "FO11: dismissing again moves the one row's date forward rather than adding another")]
    public async Task Dismissing_Twice_UpsertsOneRow()
    {
        long activityId;
        await using (var db = CreateDbContext())
        {
            activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "fo11-dismiss-twice", ct: CancellationToken);
            await SeedDismissalAsync(db, activityId, RecurrenceType.DayOfMonth, 5, Today().AddDays(-200));
        }

        (await DismissAsync(activityId, RecurrenceType.DayOfMonth, 5)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await DismissAsync(activityId, RecurrenceType.DayOfMonth, 5)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var assertDb = CreateDbContext();
        var rows = await assertDb.Set<PlannerSuggestionDismissal>().IgnoreQueryFilters()
            .Where(d => d.ActivityId == activityId).ToListAsync(CancellationToken);
        rows.Should().ContainSingle().Which.DismissedOn.Should().Be(Today());
    }

    [Theory(DisplayName = "FO11: only a weekday or day-of-month pattern with an in-range value can be dismissed")]
    [InlineData(RecurrenceType.DateRange, 1)]
    [InlineData(RecurrenceType.DayType, 1)]
    [InlineData(RecurrenceType.DayOfWeek, 0)]
    [InlineData(RecurrenceType.DayOfWeek, 8)]
    [InlineData(RecurrenceType.DayOfMonth, 32)]
    public async Task Dismiss_RejectsWhatIsNotAPattern(RecurrenceType patternType, int patternValue)
    {
        long activityId;
        await using (var db = CreateDbContext())
            activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "fo11-dismiss-invalid", ct: CancellationToken);

        (await DismissAsync(activityId, patternType, patternValue)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await using var assertDb = CreateDbContext();
        (await assertDb.Set<PlannerSuggestionDismissal>().IgnoreQueryFilters().AnyAsync(CancellationToken)).Should().BeFalse();
    }

    [Fact(DisplayName = "FO11: another user's activity cannot be dismissed")]
    public async Task Dismiss_ForeignActivity_IsNotFound()
    {
        var foreignUserId = await ActivityMergeTestSupport.SecondUserAsync(
            Fixture, $"fo11-dismiss-foreign-{Guid.NewGuid():N}@test.com");
        long foreignActivityId;
        await using (var db = CreateDbContext())
            foreignActivityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "fo11-foreign", foreignUserId, CancellationToken);

        (await DismissAsync(foreignActivityId, RecurrenceType.DayOfWeek, MondayIso)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using var assertDb = CreateDbContext();
        (await assertDb.Set<PlannerSuggestionDismissal>().IgnoreQueryFilters().AnyAsync(CancellationToken)).Should().BeFalse();
    }

    /// <summary>
    /// FO11. A dismissal is the user's own decision, so a merge carries it: left behind, it would cascade away
    /// with the merged activity and bring the pattern straight back. The same pattern dismissed on both sides
    /// collapses to one row with the later date; a pattern only the merged side dismissed moves across.
    /// </summary>
    [Fact(DisplayName = "FO11: merging activities carries their dismissals, collapsing duplicates to the latest")]
    public async Task Merge_CarriesDismissals()
    {
        var today = Today();
        long survivorId, mergedId;
        await using (var db = CreateDbContext())
        {
            survivorId = await PlanningTestSeedHelper.SeedActivityAsync(db, "fo11-merge-survivor", ct: CancellationToken);
            mergedId = await PlanningTestSeedHelper.SeedActivityAsync(db, "fo11-merge-merged", ct: CancellationToken);
            await SeedDismissalAsync(db, survivorId, RecurrenceType.DayOfWeek, MondayIso, today.AddDays(-30));
            await SeedDismissalAsync(db, mergedId, RecurrenceType.DayOfWeek, MondayIso, today.AddDays(-2));
            await SeedDismissalAsync(db, mergedId, RecurrenceType.DayOfMonth, 15, today.AddDays(-10));
        }

        var response = await CreateClient().PostAsJsonAsync("api/activity/merge",
            new { survivorId, mergedIds = new[] { mergedId } }, JsonOpts, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(CancellationToken));

        await using var assertDb = CreateDbContext();
        var rows = await assertDb.Set<PlannerSuggestionDismissal>().IgnoreQueryFilters()
            .OrderBy(d => d.PatternType)
            .Select(d => new { d.ActivityId, d.PatternType, d.PatternValue, d.DismissedOn })
            .ToListAsync(CancellationToken);
        rows.Should().BeEquivalentTo([
            new { ActivityId = survivorId, PatternType = RecurrenceType.DayOfWeek, PatternValue = MondayIso, DismissedOn = today.AddDays(-2) },
            new { ActivityId = survivorId, PatternType = RecurrenceType.DayOfMonth, PatternValue = 15, DismissedOn = today.AddDays(-10) }
        ]);
    }

    // ─── helpers ─────────────────────────────────────────────────────────────

    private const int MondayIso = 1;

    /// <summary>
    /// The views' window is measured from the database's <c>current_date</c>, so targets are relative to the real
    /// clock. Fixed future dates used to stand in here and would have aged out of the window — every pattern test
    /// would have started failing on its own a few months on.
    /// </summary>
    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);

    /// <summary>The first Monday after today, so the three sittings a week apart before it are all recent.</summary>
    private static DateOnly UpcomingMonday()
    {
        var today = Today();
        var days = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
        return today.AddDays(days == 0 ? 7 : days);
    }

    private static async Task SeedTaskOnAsync(Microsoft.EntityFrameworkCore.DbContext db, long activityId, DateOnly date)
    {
        var calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, date);
        await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, calendarId, new TimeOnly(9, 0), new TimeOnly(10, 0));
    }

    private static async Task SeedDismissalAsync(Microsoft.EntityFrameworkCore.DbContext db, long activityId,
        RecurrenceType patternType, int patternValue, DateOnly dismissedOn)
    {
        db.Set<PlannerSuggestionDismissal>().Add(new PlannerSuggestionDismissal
        {
            UserId = PlanningTestSeedHelper.TestUserId,
            ActivityId = activityId,
            PatternType = patternType,
            PatternValue = patternValue,
            DismissedOn = dismissedOn
        });
        await db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task<HttpResponseMessage> DismissAsync(long activityId, RecurrenceType patternType, int patternValue) =>
        await CreateClient().PostAsJsonAsync("api/repeating-planner-task/suggestions/dismiss",
            new { activityId, patternType, patternValue }, JsonOpts, CancellationToken);

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

    /// <summary>
    /// A completed four-period Pomodoro from 09:00 UTC: 25-minute focus periods on <paramref name="focusActivityId"/>,
    /// 5-minute short breaks on <paramref name="restActivityId"/>, then a 15-minute long break back on the focus
    /// activity — the case where only the phase, not the activity, says it is a break.
    /// </summary>
    private static async Task SeedPomodoroAsync(Microsoft.EntityFrameworkCore.DbContext db, long focusActivityId, long restActivityId, DateOnly date)
    {
        var session = new ActivityHistorySession
        {
            UserId = PlanningTestSeedHelper.TestUserId,
            Kind = TimerSessionKind.Pomodoro,
            Outcome = TimerSessionOutcome.Completed,
            PlannedFocusPeriods = 4,
            CompletedFocusPeriods = 4
        };
        db.Set<ActivityHistorySession>().Add(session);
        await db.SaveChangesAsync(CancellationToken.None);

        var at = date.ToDateTime(new TimeOnly(9, 0), DateTimeKind.Utc);
        for (var period = 1; period <= 4; period++)
        {
            await SeedActivityHistoryAsync(db, focusActivityId, at, at.AddMinutes(25), session.Id, TimerSessionPhase.Focus);
            at = at.AddMinutes(25);
            if (period < 4)
            {
                await SeedActivityHistoryAsync(db, restActivityId, at, at.AddMinutes(5), session.Id, TimerSessionPhase.ShortBreak);
                at = at.AddMinutes(5);
            }
        }
        await SeedActivityHistoryAsync(db, focusActivityId, at, at.AddMinutes(15), session.Id, TimerSessionPhase.LongBreak);
    }

    private static async Task SeedActivityHistoryAsync(Microsoft.EntityFrameworkCore.DbContext db, long activityId, DateTime startUtc, DateTime endUtc,
        long? sessionId = null, TimerSessionPhase? phase = null)
    {
        var length = endUtc - startUtc;
        db.Set<ActivityHistory>().Add(new ActivityHistory
        {
            UserId = PlanningTestSeedHelper.TestUserId,
            ActivityId = activityId,
            StartTimestamp = startUtc,
            EndTimestamp = endUtc,
            Length = new IntTime((int)length.TotalHours, length.Minutes),
            SessionId = sessionId,
            Phase = phase
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
