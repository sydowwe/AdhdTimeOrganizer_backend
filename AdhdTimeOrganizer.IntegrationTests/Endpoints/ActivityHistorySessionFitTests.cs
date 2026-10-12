using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.History.application.dto.response.activityHistory;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.History.domain.model.@enum;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Sydowwe.Framework.domain.valueObject;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// F2-07: <c>GET /activity-history/session/fit</c>, asserted on the length a seeded set of sessions produces.
/// <para>
/// Every way this goes wrong still answers 200 with a plausible length. A break row read as focus is a five-minute
/// "stretch" in every Pomodoro. Another activity's sessions, or a session that ran out while nobody was there, move
/// the figure without a trace. So each case seeds sessions whose only correct answer differs from the wrong ones.
/// Sessions are written straight through the DbContext, which publishes no <c>ActivityTimeRecordedEvent</c>.
/// </para>
/// </summary>
[Collection("Postgres")]
public class ActivityHistorySessionFitTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    private static readonly DateTime Today = DateTime.UtcNow.Date;

    [Fact(DisplayName = "F2-07: countdowns usually stopped around minute 12 offer 10 minutes")]
    public async Task UsuallyStoppedEarly_OffersTheReachedLength()
    {
        var activityId = await SeedActivityAsync("thesis");
        for (var day = 1; day <= 5; day++)
            await SeedCountdownAsync(activityId, Today.AddDays(-day), minutes: 12, TimerSessionOutcome.StoppedEarly);
        await SeedCountdownAsync(activityId, Today.AddDays(-6), minutes: 25, TimerSessionOutcome.Completed);

        var fit = await GetFitAsync(activityId);

        fit.SuggestedSeconds.Should().Be(10 * 60);
        fit.SampleCount.Should().Be(6);
    }

    /// <summary>
    /// The rest activity is the focus activity here, so only the phase tells a five-minute break from focus. Read as
    /// focus rows, a break would take the second "completed" slot and push the second real period out of it, so
    /// fewer stretches would bear on the offer. <c>SampleCount</c> is what catches that.
    /// </summary>
    [Fact(DisplayName = "F2-07: a Pomodoro's breaks are not focus, even on the same activity")]
    public async Task PomodoroBreaks_AreNotFocus()
    {
        var activityId = await SeedActivityAsync("same-rest");
        for (var day = 1; day <= 5; day++)
            await SeedPomodoroAsync(activityId, Today.AddDays(-day), completedFocusPeriods: 2, focusMinutes: [25, 25, 20],
                TimerSessionOutcome.StoppedEarly);

        var fit = await GetFitAsync(activityId);

        // Ten stretches ran 25 minutes and five were stopped at 20, so 20 minutes is reached by all fifteen and 25 by
        // ten of fifteen (67%): 20 is the largest length most of them reach.
        fit.SuggestedSeconds.Should().Be(20 * 60);
        fit.SampleCount.Should().Be(15);
    }

    [Fact(DisplayName = "F2-07: other activities, sessions that ran out while away, and old sessions do not count")]
    public async Task OnlyThisActivitysRecentAttendedSessions()
    {
        var activityId = await SeedActivityAsync("scoped");
        var otherId = await SeedActivityAsync("scoped-other");
        for (var day = 1; day <= 4; day++)
            await SeedCountdownAsync(activityId, Today.AddDays(-day), minutes: 25, TimerSessionOutcome.Completed);

        // Each of these would be the fifth session.
        await SeedCountdownAsync(otherId, Today.AddDays(-1), minutes: 25, TimerSessionOutcome.Completed);
        // Afternoon: (activity, start) is unique, and the countdowns start at 09:00.
        await SeedPomodoroAsync(activityId, Today.AddDays(-2).AddHours(5), completedFocusPeriods: 1, focusMinutes: [25, 25],
            TimerSessionOutcome.RanOutWhileAway);
        await SeedCountdownAsync(activityId, Today.AddDays(-TimerLengthFitWindowDays - 1), minutes: 25,
            TimerSessionOutcome.Completed);

        var fit = await GetFitAsync(activityId);

        fit.SuggestedSeconds.Should().BeNull("four sessions are below the threshold");
        fit.SampleCount.Should().BeNull();
    }

    [Fact(DisplayName = "F2-07: a missing activityId is a 400")]
    public async Task MissingActivityId_Is400()
    {
        var response = await CreateClient().GetAsync("api/activity-history/session/fit", CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---------------------------------------------------------------- helpers

    private const int TimerLengthFitWindowDays = AdhdTimeOrganizer.History.domain.service.TimerLengthFit.WindowDays;

    private async Task<long> SeedActivityAsync(string name)
    {
        await using var db = CreateDbContext();
        return await PlanningTestSeedHelper.SeedActivityAsync(db, $"fit-{name}", UserId, CancellationToken);
    }

    private async Task SeedCountdownAsync(long activityId, DateTime day, int minutes, TimerSessionOutcome outcome)
    {
        var start = day.AddHours(9);
        await SaveSessionAsync(new ActivityHistorySession
        {
            UserId = UserId,
            Kind = TimerSessionKind.Timer,
            Outcome = outcome,
            PlannedLength = new IntTime(25 * 60),
            Records = [Row(activityId, start, minutes, phase: null)]
        });
    }

    /// <summary>Focus periods with a five-minute break after each but the last, all on the one activity.</summary>
    private async Task SeedPomodoroAsync(
        long activityId, DateTime day, int completedFocusPeriods, int[] focusMinutes, TimerSessionOutcome outcome)
    {
        var start = day.AddHours(9);
        var records = new List<ActivityHistory>();
        for (var i = 0; i < focusMinutes.Length; i++)
        {
            records.Add(Row(activityId, start, focusMinutes[i], TimerSessionPhase.Focus));
            start = start.AddMinutes(focusMinutes[i]);
            if (i == focusMinutes.Length - 1) break;
            records.Add(Row(activityId, start, 5, TimerSessionPhase.ShortBreak));
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

    private static ActivityHistory Row(long activityId, DateTime start, int minutes, TimerSessionPhase? phase) => new()
    {
        UserId = UserId,
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

    private async Task<ActivityHistorySessionFitResponse> GetFitAsync(long activityId)
    {
        var response = await CreateClient().GetAsync($"api/activity-history/session/fit?activityId={activityId}", CancellationToken);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return (await response.Content.ReadFromJsonAsync<ActivityHistorySessionFitResponse>(JsonOpts, CancellationToken))!;
    }
}
