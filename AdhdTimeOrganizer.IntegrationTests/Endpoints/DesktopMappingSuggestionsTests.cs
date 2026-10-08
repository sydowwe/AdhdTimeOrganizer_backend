using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.History.domain.model.@enum;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.desktop;
using FluentAssertions;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.domain.valueObject;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// FO9 (F3-02) — <c>GET /activity-tracking/desktop/mapping-suggestions</c>: an uncovered process the user's own timer
/// focus phases point at one activity is offered as a rule. Every case seeds real timer sessions and desktop ledger
/// minutes and asserts on what comes back, because a wrong suggestion is silent and, once accepted, permanent.
/// </summary>
/// <remarks>
/// The thresholds (3 sittings, 10 minutes, 70 % under one activity) are <c>MappingSuggestionRule</c>'s; the cases
/// here sit on either side of them. Sittings are placed on the previous days at 10:00 UTC, inside the default 30-day
/// window whatever the hour the suite runs.
/// </remarks>
[Collection("Postgres")]
public class DesktopMappingSuggestionsTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    private const string Route = "api/activity-tracking/desktop/mapping-suggestions";

    private static DateTime DayAt10(int daysAgo) =>
        DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-daysAgo).ToDateTime(new TimeOnly(10, 0), DateTimeKind.Utc);

    [Fact(DisplayName = "A process active through three timer sittings on one activity is suggested for it")]
    public async Task ProcessSeenInThreeSittings_IsSuggested()
    {
        var thesis = await SeedActivityAsync("Thesis");
        var rest = await SeedActivityAsync("Rest");
        await SeedTimerAsync(thesis, DayAt10(1), 10);
        await SeedTimerAsync(thesis, DayAt10(2), 10);
        // A pomodoro: only its focus phase teaches. The break runs under Rest and winword stays open through it.
        await SeedPomodoroAsync(thesis, rest, DayAt10(3), focusMinutes: 10, breakMinutes: 5);
        await SeedMinutesAsync("winword.exe", DayAt10(1), 10);
        await SeedMinutesAsync("winword.exe", DayAt10(2), 10);
        await SeedMinutesAsync("winword.exe", DayAt10(3), 15);

        var suggestion = (await GetAsync()).Should().ContainSingle().Subject;

        suggestion.GetProperty("processName").GetString().Should().Be("winword.exe");
        suggestion.GetProperty("activityId").GetInt64().Should().Be(thesis);
        suggestion.GetProperty("activityName").GetString().Should().StartWith("Thesis");
        suggestion.GetProperty("sessionCount").GetInt32().Should().Be(3);
        suggestion.GetProperty("activitySessionCount").GetInt32().Should().Be(3);
        suggestion.GetProperty("seconds").GetInt32().Should().Be(30 * 60,
            "the five minutes during the break are not focus time and teach nothing");
    }

    [Fact(DisplayName = "Two sittings are not enough")]
    public async Task TwoSittings_AreNotEnough()
    {
        var thesis = await SeedActivityAsync("Thesis");
        await SeedTimerAsync(thesis, DayAt10(1), 20);
        await SeedTimerAsync(thesis, DayAt10(2), 20);
        await SeedMinutesAsync("winword.exe", DayAt10(1), 20);
        await SeedMinutesAsync("winword.exe", DayAt10(2), 20);

        (await GetAsync()).Should().BeEmpty();
    }

    /// <summary>
    /// The browser case: used through Thesis and Email timers alike, it is under 70 % for either, and a process-level
    /// rule would mislabel half of it.
    /// </summary>
    [Fact(DisplayName = "A process split across activities is not suggested")]
    public async Task ProcessSplitAcrossActivities_IsNotSuggested()
    {
        var thesis = await SeedActivityAsync("Thesis");
        var email = await SeedActivityAsync("Email");
        for (var day = 1; day <= 3; day++)
        {
            await SeedTimerAsync(thesis, DayAt10(day), 10);
            await SeedTimerAsync(email, DayAt10(day).AddHours(2), 10);
            await SeedMinutesAsync("chrome.exe", DayAt10(day), 10);
            await SeedMinutesAsync("chrome.exe", DayAt10(day).AddHours(2), 10);
        }

        (await GetAsync()).Should().BeEmpty();
    }

    [Fact(DisplayName = "A process an active rule already covers is not suggested")]
    public async Task CoveredProcess_IsNotSuggested()
    {
        var thesis = await SeedActivityAsync("Thesis");
        for (var day = 1; day <= 3; day++)
        {
            await SeedTimerAsync(thesis, DayAt10(day), 10);
            await SeedMinutesAsync("winword.exe", DayAt10(day), 10);
        }
        await using (var db = CreateDbContext())
        {
            db.Set<TrackerDesktopMappingByPattern>().Add(new TrackerDesktopMappingByPattern
            {
                UserId = UserId,
                ProcessName = "winword",
                ProcessNameMatchType = PatternMatchType.Contains,
                IsActive = true,
                IsIgnored = true
            });
            await db.SaveChangesAsync(CancellationToken);
        }

        (await GetAsync()).Should().BeEmpty("an ignore rule is a decision too");
    }

    /// <summary>
    /// The seam guard. A plain row — what the stopwatch, a hand-logged entry and the tracker itself all write — looks
    /// like evidence, and learning from tracker rows would feed the rules back into themselves.
    /// </summary>
    [Fact(DisplayName = "Rows outside a timer session teach nothing")]
    public async Task NonTimerRows_TeachNothing()
    {
        var thesis = await SeedActivityAsync("Thesis");
        await using (var db = CreateDbContext())
        {
            for (var day = 1; day <= 3; day++)
                db.Set<ActivityHistory>().Add(new ActivityHistory
                {
                    UserId = UserId,
                    ActivityId = thesis,
                    StartTimestamp = DayAt10(day),
                    EndTimestamp = DayAt10(day).AddMinutes(20),
                    Length = new IntTime(20 * 60)
                });
            await db.SaveChangesAsync(CancellationToken);
        }
        for (var day = 1; day <= 3; day++)
            await SeedMinutesAsync("winword.exe", DayAt10(day), 20);

        (await GetAsync()).Should().BeEmpty();
    }

    [Fact(DisplayName = "A window over 90 days is a 400")]
    public async Task DaysOutOfRange_Is400()
    {
        var response = await CreateUserRoleClient().GetAsync($"{Route}?days=91", CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---- helpers ---------------------------------------------------------------------------------

    private async Task<List<JsonElement>> GetAsync()
    {
        var response = await CreateUserRoleClient().GetAsync(Route, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(CancellationToken));
        return (await response.Content.ReadFromJsonAsync<List<JsonElement>>(JsonOpts, CancellationToken))!;
    }

    private async Task<long> SeedActivityAsync(string name)
    {
        await using var db = CreateDbContext();
        return await PlanningTestSeedHelper.SeedActivityAsync(db, $"{name}-{Guid.NewGuid():N}", ct: CancellationToken);
    }

    private async Task SeedTimerAsync(long activityId, DateTime start, int minutes)
    {
        await using var db = CreateDbContext();
        var session = new ActivityHistorySession { UserId = UserId, Kind = TimerSessionKind.Timer, Outcome = TimerSessionOutcome.Completed };
        db.Set<ActivityHistory>().Add(Row(activityId, start, minutes, session, phase: null));
        await db.SaveChangesAsync(CancellationToken);
    }

    private async Task SeedPomodoroAsync(long focusId, long restId, DateTime start, int focusMinutes, int breakMinutes)
    {
        await using var db = CreateDbContext();
        var session = new ActivityHistorySession
        {
            UserId = UserId,
            Kind = TimerSessionKind.Pomodoro,
            Outcome = TimerSessionOutcome.Completed,
            PlannedFocusPeriods = 1,
            CompletedFocusPeriods = 1
        };
        db.Set<ActivityHistory>().Add(Row(focusId, start, focusMinutes, session, TimerSessionPhase.Focus));
        db.Set<ActivityHistory>().Add(Row(restId, start.AddMinutes(focusMinutes), breakMinutes, session, TimerSessionPhase.ShortBreak));
        await db.SaveChangesAsync(CancellationToken);
    }

    private static ActivityHistory Row(long activityId, DateTime start, int minutes, ActivityHistorySession session, TimerSessionPhase? phase) =>
        new()
        {
            UserId = UserId,
            ActivityId = activityId,
            StartTimestamp = start,
            EndTimestamp = start.AddMinutes(minutes),
            Length = new IntTime(minutes * 60),
            Session = session,
            Phase = phase
        };

    /// <summary>One fully active desktop ledger minute per minute of the stretch, as the heartbeat writes them.</summary>
    private async Task SeedMinutesAsync(string processName, DateTime start, int minutes)
    {
        await using var db = CreateDbContext();
        for (var minute = 0; minute < minutes; minute++)
            db.Set<DesktopActivityEntry>().Add(new DesktopActivityEntry
            {
                UserId = UserId,
                WindowStart = start.AddMinutes(minute),
                ProcessName = processName,
                ProductName = processName,
                WindowTitle = "Document",
                ExecutablePath = $@"C:\apps\{processName}",
                IsFullscreen = false,
                ActiveSeconds = 60,
                BackgroundSeconds = 0,
                IsPlayingSound = false,
                ActiveMonitor = 0
            });
        await db.SaveChangesAsync(CancellationToken);
    }
}
