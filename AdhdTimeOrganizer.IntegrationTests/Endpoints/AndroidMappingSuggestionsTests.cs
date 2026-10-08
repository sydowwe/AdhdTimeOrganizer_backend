using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.History.domain.model.@enum;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.android;
using FluentAssertions;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.domain.valueObject;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// FO9 (F3-02) — <c>GET /activity-tracking/android/mapping-suggestions</c>, the android twin of
/// <see cref="DesktopMappingSuggestionsTests"/>. The thresholds the two share are pinned there; these cases pin what
/// is android's own: phone sessions are clipped to the timer, and an app must fill the timers it appears in, so the
/// feed checked through every pomodoro is not offered as the work.
/// </summary>
[Collection("Postgres")]
public class AndroidMappingSuggestionsTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    private const string Route = "api/activity-tracking/android/mapping-suggestions";

    private static DateTime DayAt10(int daysAgo) =>
        DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-daysAgo).ToDateTime(new TimeOnly(10, 0), DateTimeKind.Utc);

    [Fact(DisplayName = "An app that fills three timer sittings on one activity is suggested, counted only inside them")]
    public async Task AppFillingThreeSittings_IsSuggested()
    {
        var language = await SeedActivityAsync("Language");
        for (var day = 1; day <= 3; day++)
        {
            await SeedTimerAsync(language, DayAt10(day), 20);
            // Opened five minutes before the timer was started: those five minutes are not focus time.
            await SeedSessionAsync("com.duolingo", "Duolingo", DayAt10(day).AddMinutes(-5), 20);
        }

        var suggestion = (await GetAsync()).Should().ContainSingle().Subject;

        suggestion.GetProperty("packageName").GetString().Should().Be("com.duolingo");
        suggestion.GetProperty("appLabel").GetString().Should().Be("Duolingo");
        suggestion.GetProperty("activityId").GetInt64().Should().Be(language);
        suggestion.GetProperty("activityName").GetString().Should().StartWith("Language");
        suggestion.GetProperty("sessionCount").GetInt32().Should().Be(3);
        suggestion.GetProperty("activitySessionCount").GetInt32().Should().Be(3);
        suggestion.GetProperty("seconds").GetInt32().Should().Be(3 * 15 * 60, "only the overlap with each timer counts");
    }

    /// <summary>
    /// The case the desktop does not have. Five minutes of a feed in each of four Thesis pomodoros passes every shared
    /// threshold — four sittings, twenty minutes, all of it under Thesis — and is exactly the rule not to offer.
    /// </summary>
    [Fact(DisplayName = "An app glanced at through every timer is not suggested")]
    public async Task AppThatDoesNotFillTheTimers_IsNotSuggested()
    {
        var thesis = await SeedActivityAsync("Thesis");
        for (var day = 1; day <= 4; day++)
        {
            await SeedTimerAsync(thesis, DayAt10(day), 25);
            await SeedSessionAsync("com.instagram.android", "Instagram", DayAt10(day).AddMinutes(10), 5);
        }

        (await GetAsync()).Should().BeEmpty();
    }

    [Fact(DisplayName = "An app an active rule already covers is not suggested")]
    public async Task CoveredApp_IsNotSuggested()
    {
        var language = await SeedActivityAsync("Language");
        for (var day = 1; day <= 3; day++)
        {
            await SeedTimerAsync(language, DayAt10(day), 20);
            await SeedSessionAsync("com.duolingo", "Duolingo", DayAt10(day), 20);
        }
        await using (var db = CreateDbContext())
        {
            db.Set<TrackerAndroidMappingByPattern>().Add(new TrackerAndroidMappingByPattern
            {
                UserId = UserId,
                AppLabel = "duo",
                AppLabelMatchType = PatternMatchType.Contains,
                IsActive = true,
                IsIgnored = true
            });
            await db.SaveChangesAsync(CancellationToken);
        }

        (await GetAsync()).Should().BeEmpty("an ignore rule is a decision too");
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
        db.Set<ActivityHistory>().Add(new ActivityHistory
        {
            UserId = UserId,
            ActivityId = activityId,
            StartTimestamp = start,
            EndTimestamp = start.AddMinutes(minutes),
            Length = new IntTime(minutes * 60),
            Session = new ActivityHistorySession { UserId = UserId, Kind = TimerSessionKind.Timer, Outcome = TimerSessionOutcome.Completed }
        });
        await db.SaveChangesAsync(CancellationToken);
    }

    private async Task SeedSessionAsync(string packageName, string appLabel, DateTime start, int minutes)
    {
        await using var db = CreateDbContext();
        db.Set<AndroidSessionData>().Add(new AndroidSessionData
        {
            UserId = UserId,
            DeviceId = "pixel",
            PackageName = packageName,
            AppLabel = appLabel,
            SessionStartUtc = start,
            SessionEndUtc = start.AddMinutes(minutes),
            DurationSeconds = minutes * 60
        });
        await db.SaveChangesAsync(CancellationToken);
    }
}
