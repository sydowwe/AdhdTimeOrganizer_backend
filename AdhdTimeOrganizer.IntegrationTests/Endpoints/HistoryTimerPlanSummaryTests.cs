using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.History.application.dto.response.activityHistory.dashboard;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.History.domain.model.@enum;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.IntegrationTests.Reminders;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.domain.valueObject;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// <c>POST /activity-history/dashboard/summary/timer-plan</c>: the focus a range's timers were set for against the
/// focus they got. Every way this goes wrong still answers 200 with plausible totals. A break read as focus adds
/// five minutes per Pomodoro break; a session that ran out while nobody was there turns an abandoned plan into a
/// met one; a completed countdown's one-minute rounding gap shows up as a shortfall; a UTC range swaps one edge
/// session for another. So each case seeds sessions whose only correct total differs from every wrong one.
/// <para>
/// Sessions are written straight through the DbContext, as in <c>ActivityHistorySessionFitTests</c>. The user is
/// in Bratislava (UTC+2 in July), and the range is <c>ThreeDays</c> over 15 July: 12–15 July on the user's clock.
/// </para>
/// </summary>
[Collection("Postgres")]
public class HistoryTimerPlanSummaryTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    private const string Route = "/api/activity-history/dashboard/summary/timer-plan";

    private static readonly TimeZoneInfo Bratislava = TimeZoneInfo.FindSystemTimeZoneById("Europe/Bratislava");

    private static readonly DateOnly SummerDay = new(2026, 7, 15);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected override async Task SeedAsync(DbContext db)
    {
        var user = await db.Set<User>().IgnoreQueryFilters().SingleAsync(u => u.Id == UserId);
        user.Timezone = Bratislava;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// The five session shapes the line has to tell apart, in one range:
    /// <list type="bullet">
    /// <item>a countdown completed a minute under its 25-minute plan (rounding, not a shortfall);</item>
    /// <item>a countdown stopped at 12 of 25 minutes (13 minutes short);</item>
    /// <item>a Pomodoro stopped after 2 of 4 periods: plan 4 × 25, focus 25 + 25 + 10, its two five-minute breaks
    /// logged on the focus activity (40 minutes short, and 60 of focus, not 70);</item>
    /// <item>a Pomodoro that ran out while nobody was there, counted and summed into nothing;</item>
    /// <item>a pre-OP6 session with no plan, counted only in <c>withoutPlanCount</c>.</item>
    /// </list>
    /// </summary>
    [Fact(DisplayName = "OP6 trend: outcomes split the planned sessions, and the sums cover the same ones")]
    public async Task Totals_SplitByOutcome_AndSumOnlyTheComparedSessions()
    {
        var activityId = await SeedActivityAsync("timer-plan", UserId);

        await SeedCountdownAsync(activityId, Utc(13, 7, 0), plannedMinutes: 25, minutes: 24, TimerSessionOutcome.Completed);
        await SeedCountdownAsync(activityId, Utc(13, 9, 0), plannedMinutes: 25, minutes: 12, TimerSessionOutcome.StoppedEarly);
        await SeedPomodoroAsync(activityId, Utc(14, 7, 0), completedFocusPeriods: 2, focusMinutes: [25, 25, 10],
            TimerSessionOutcome.StoppedEarly);
        await SeedPomodoroAsync(activityId, Utc(14, 12, 0), completedFocusPeriods: 2, focusMinutes: [25, 25],
            TimerSessionOutcome.RanOutWhileAway);
        await SeedCountdownAsync(activityId, Utc(14, 15, 0), plannedMinutes: null, minutes: 30, TimerSessionOutcome.Completed);

        var plan = await PostAsync();

        plan.SessionCount.Should().Be(4, "four sessions carry a plan; the pre-OP6 one does not");
        plan.WithoutPlanCount.Should().Be(1);
        plan.CompletedCount.Should().Be(1);
        plan.StoppedEarlyCount.Should().Be(2);
        plan.RanOutWhileAwayCount.Should().Be(1, "it is counted, so the client can name it");

        plan.PlannedFocusSeconds.Should().Be((25 + 25 + 100) * 60,
            "the session that ran out while away is not in the planned sum -- with it, 250 minutes");
        plan.ActualFocusSeconds.Should().Be((24 + 12 + 60) * 60,
            "the Pomodoro's breaks are on the focus activity and must not count -- with them, 106 minutes");
        plan.StoppedEarlyShortfallSeconds.Should().Be((13 + 40) * 60,
            "only stopped-early sessions fall short -- the completed countdown's rounding minute would make 54");
    }

    /// <summary>
    /// A session belongs to the day its first row starts on the user's clock. Each edge row sits in the two hours
    /// where Bratislava's date and UTC's disagree, so a UTC range swaps the included session for an excluded one.
    /// </summary>
    [Fact(DisplayName = "OP6 trend: the range runs from the user's midnight to the user's midnight")]
    public async Task RangeEdges_AreTheUsersMidnights()
    {
        var activityId = await SeedActivityAsync("timer-plan-edges", UserId);

        // 11 July 23:30 local: the evening before the range.
        await SeedCountdownAsync(activityId, Utc(11, 21, 30), plannedMinutes: 45, minutes: 45, TimerSessionOutcome.Completed);
        // 12 July 00:30 local, still 11 July in UTC: the range's first half hour.
        await SeedCountdownAsync(activityId, Utc(11, 22, 30), plannedMinutes: 20, minutes: 20, TimerSessionOutcome.Completed);
        // 16 July 00:30 local, still 15 July in UTC: just past the range.
        await SeedCountdownAsync(activityId, Utc(15, 22, 30), plannedMinutes: 50, minutes: 50, TimerSessionOutcome.Completed);

        var plan = await PostAsync();

        plan.SessionCount.Should().Be(1);
        plan.PlannedFocusSeconds.Should().Be(20 * 60,
            "only the 00:30 session on 12 July is inside; a UTC range would hold the 16 July one instead");
    }

    /// <summary>
    /// Every read is filtered on the caller. Another user's sessions in the same range must leave all eight figures
    /// at zero, not only the sums.
    /// </summary>
    [Fact(DisplayName = "OP6 trend: another user's sessions are not counted")]
    public async Task AnotherUsersSessions_AreIgnored()
    {
        await using (var db = CreateDbContext())
            await ReminderSeedHelper.EnsureOtherUserAsync(db, CancellationToken);
        var theirActivityId = await SeedActivityAsync("timer-plan-theirs", ReminderSeedHelper.OtherUserId);

        await SeedCountdownAsync(theirActivityId, Utc(13, 8, 0), plannedMinutes: 90, minutes: 30,
            TimerSessionOutcome.StoppedEarly, ReminderSeedHelper.OtherUserId);
        await SeedCountdownAsync(theirActivityId, Utc(13, 10, 0), plannedMinutes: null, minutes: 30,
            TimerSessionOutcome.Completed, ReminderSeedHelper.OtherUserId);

        var plan = await PostAsync();

        plan.Should().BeEquivalentTo(new HistoryTimerPlanResponse
        {
            SessionCount = 0,
            WithoutPlanCount = 0,
            PlannedFocusSeconds = 0,
            ActualFocusSeconds = 0,
            CompletedCount = 0,
            StoppedEarlyCount = 0,
            RanOutWhileAwayCount = 0,
            StoppedEarlyShortfallSeconds = 0
        });
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>An instant in July 2026, given in UTC.</summary>
    private static DateTime Utc(int day, int hour, int minute) => new(2026, 7, day, hour, minute, 0, DateTimeKind.Utc);

    private async Task<long> SeedActivityAsync(string name, long userId)
    {
        await using var db = CreateDbContext();
        return await PlanningTestSeedHelper.SeedActivityAsync(db, name, userId, CancellationToken);
    }

    private async Task SeedCountdownAsync(
        long activityId, DateTime start, int? plannedMinutes, int minutes, TimerSessionOutcome outcome, long userId = UserId)
    {
        await SaveSessionAsync(new ActivityHistorySession
        {
            UserId = userId,
            Kind = TimerSessionKind.Timer,
            Outcome = outcome,
            PlannedLength = plannedMinutes is { } planned ? new IntTime(planned * 60) : null,
            Records = [Row(activityId, start, minutes, phase: null, userId)]
        });
    }

    /// <summary>Focus periods of a 4 × 25 plan, with a five-minute break after each but the last, all on one activity.</summary>
    private async Task SeedPomodoroAsync(
        long activityId, DateTime start, int completedFocusPeriods, int[] focusMinutes, TimerSessionOutcome outcome)
    {
        var records = new List<ActivityHistory>();
        for (var i = 0; i < focusMinutes.Length; i++)
        {
            records.Add(Row(activityId, start, focusMinutes[i], TimerSessionPhase.Focus, UserId));
            start = start.AddMinutes(focusMinutes[i]);
            if (i == focusMinutes.Length - 1) break;
            records.Add(Row(activityId, start, 5, TimerSessionPhase.ShortBreak, UserId));
            start = start.AddMinutes(5);
        }

        await SaveSessionAsync(new ActivityHistorySession
        {
            UserId = UserId,
            Kind = TimerSessionKind.Pomodoro,
            Outcome = outcome,
            PlannedFocusPeriods = 4,
            CompletedFocusPeriods = completedFocusPeriods,
            PlannedLength = new IntTime(4 * 25 * 60),
            Records = records
        });
    }

    private static ActivityHistory Row(long activityId, DateTime start, int minutes, TimerSessionPhase? phase, long userId) => new()
    {
        UserId = userId,
        ActivityId = activityId,
        StartTimestamp = start,
        EndTimestamp = start.AddMinutes(minutes),
        Length = new IntTime(minutes * 60),
        Phase = phase
    };

    private async Task SaveSessionAsync(ActivityHistorySession session)
    {
        await using var db = CreateDbContext();
        db.Set<ActivityHistorySession>().Add(session);
        await db.SaveChangesAsync(CancellationToken);
    }

    private async Task<HistoryTimerPlanResponse> PostAsync()
    {
        var response = await CreateUserRoleClient().PostAsJsonAsync(
            Route, new { date = SummerDay, rangeType = "ThreeDays" }, Json, CancellationToken);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return (await response.Content.ReadFromJsonAsync<HistoryTimerPlanResponse>(Json, CancellationToken))!;
    }
}
