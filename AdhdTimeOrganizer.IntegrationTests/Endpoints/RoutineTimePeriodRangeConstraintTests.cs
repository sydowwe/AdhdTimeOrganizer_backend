using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// TEST-4 / Section E — one boundary test per <c>RoutineTimePeriodValidator</c> rule, asserting a clean 400
/// from FluentValidation rather than a 500 from <c>RoutineTimePeriodConfiguration</c>'s matching DB check
/// constraints.
/// </summary>
[Collection("Postgres")]
public class RoutineTimePeriodRangeConstraintTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private static object BasePayload(string text, int lengthInDays = 7, int streakThreshold = 90,
        int streakGraceDays = 0, int resetAnchorDay = 0, int historyDepth = 16, int? reminderLeadDays = null) => new
    {
        Text = text,
        Color = "#abcdef",
        LengthInDays = lengthInDays,
        StreakThreshold = streakThreshold,
        StreakGraceDays = streakGraceDays,
        ResetAnchorDay = resetAnchorDay,
        HistoryDepth = historyDepth,
        ReminderLeadDays = reminderLeadDays
    };

    private Task<HttpResponseMessage> CreateAsync(object payload) =>
        CreateClient().PostAsJsonAsync("api/routine-time-period", payload, JsonOpts, CancellationToken);

    [Fact(DisplayName = "LengthInDays == 0 is rejected with 400")]
    public async Task LengthInDays_Zero_Returns400() =>
        (await CreateAsync(BasePayload("len-zero", lengthInDays: 0))).StatusCode.Should().Be(HttpStatusCode.BadRequest);

    [Fact(DisplayName = "LengthInDays == 366 is rejected with 400")]
    public async Task LengthInDays_366_Returns400() =>
        (await CreateAsync(BasePayload("len-366", lengthInDays: 366, resetAnchorDay: 0))).StatusCode.Should().Be(HttpStatusCode.BadRequest);

    [Fact(DisplayName = "StreakThreshold == 0 is rejected with 400")]
    public async Task StreakThreshold_Zero_Returns400() =>
        (await CreateAsync(BasePayload("thresh-zero", streakThreshold: 0))).StatusCode.Should().Be(HttpStatusCode.BadRequest);

    [Fact(DisplayName = "StreakThreshold == 101 is rejected with 400")]
    public async Task StreakThreshold_101_Returns400() =>
        (await CreateAsync(BasePayload("thresh-101", streakThreshold: 101))).StatusCode.Should().Be(HttpStatusCode.BadRequest);

    [Fact(DisplayName = "StreakGraceDays == LengthInDays is rejected with 400 (max is LengthInDays - 1)")]
    public async Task StreakGraceDays_EqualToLengthInDays_Returns400() =>
        (await CreateAsync(BasePayload("grace-eq-length", lengthInDays: 7, streakGraceDays: 7))).StatusCode.Should().Be(HttpStatusCode.BadRequest);

    [Fact(DisplayName = "ReminderLeadDays on a one-day period is rejected with 400 (no valid lead exists)")]
    public async Task ReminderLeadDays_OnOneDayPeriod_Returns400() =>
        (await CreateAsync(BasePayload("lead-one-day", lengthInDays: 1, resetAnchorDay: 0, reminderLeadDays: 1))).StatusCode.Should().Be(HttpStatusCode.BadRequest);

    [Fact(DisplayName = "ResetAnchorDay == 8 on a weekly-aligned period (length 7) is rejected with 400")]
    public async Task ResetAnchorDay_8_OnWeeklyAlignedPeriod_Returns400() =>
        (await CreateAsync(BasePayload("anchor-8-weekly", lengthInDays: 7, resetAnchorDay: 8))).StatusCode.Should().Be(HttpStatusCode.BadRequest);

    [Fact(DisplayName = "ResetAnchorDay == 31 on a non-weekly period (length 10) is rejected with 400")]
    public async Task ResetAnchorDay_31_OnNonWeeklyPeriod_Returns400() =>
        (await CreateAsync(BasePayload("anchor-31-nonweekly", lengthInDays: 10, resetAnchorDay: 31))).StatusCode.Should().Be(HttpStatusCode.BadRequest);

    [Fact(DisplayName = "HistoryDepth == 0 is rejected with 400")]
    public async Task HistoryDepth_Zero_Returns400() =>
        (await CreateAsync(BasePayload("history-zero", historyDepth: 0))).StatusCode.Should().Be(HttpStatusCode.BadRequest);

    [Fact(DisplayName = "HistoryDepth == 101 is rejected with 400")]
    public async Task HistoryDepth_101_Returns400() =>
        (await CreateAsync(BasePayload("history-101", historyDepth: 101))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
}
