using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// TEST-4 / Section E — one boundary test per <c>RoutineTimePeriodValidator</c> rule, asserting a validation
/// failure that <b>names the property</b> rather than a check violation from
/// <c>RoutineTimePeriodConfiguration</c>'s matching DB constraint.
///
/// <para>The status code alone cannot tell those two apart, which is what let <c>CQ-3</c> hide here: three of
/// these tests were green while the validator had no <c>HistoryDepth</c> rule at all and no upper bound on
/// <c>LengthInDays</c>, because <c>DbUtils.HandleException</c> maps <c>CheckViolation</c> →
/// <c>ValidationError</c> → 400 as well. The difference the client sees is the field key: a validator failure
/// arrives under the property name and the form attaches it to an input, a mapped database error arrives under
/// a general key after a wasted round-trip. So every case here asserts on the body.</para>
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

    /// <summary>
    /// The assertion that makes this file's summary true. Status plus the presence of <paramref name="property"/>
    /// as a key in the error response — the one observable difference between the validator rejecting the value
    /// and the database doing it.
    /// </summary>
    private async Task ShouldBeValidationErrorFor(HttpResponseMessage response, string property)
    {
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, body);

        var root = JsonDocument.Parse(body).RootElement;
        root.TryGetProperty("errors", out var errors).Should().BeTrue(body);

        errors.EnumerateObject().Select(e => e.Name)
            .Should().Contain(name => string.Equals(name, property, StringComparison.OrdinalIgnoreCase),
                $"a validator rule keys the failure to {property}; a DB check violation reaches the client without it");
    }

    [Fact(DisplayName = "LengthInDays == 0 is rejected by the validator, naming the field")]
    public async Task LengthInDays_Zero_Returns400() =>
        await ShouldBeValidationErrorFor(await CreateAsync(BasePayload("len-zero", lengthInDays: 0)), "lengthInDays");

    [Fact(DisplayName = "LengthInDays == 366 is rejected by the validator, naming the field")]
    public async Task LengthInDays_366_Returns400() =>
        await ShouldBeValidationErrorFor(
            await CreateAsync(BasePayload("len-366", lengthInDays: 366, resetAnchorDay: 0)), "lengthInDays");

    [Fact(DisplayName = "StreakThreshold == 0 is rejected by the validator, naming the field")]
    public async Task StreakThreshold_Zero_Returns400() =>
        await ShouldBeValidationErrorFor(
            await CreateAsync(BasePayload("thresh-zero", streakThreshold: 0)), "streakThreshold");

    [Fact(DisplayName = "StreakThreshold == 101 is rejected by the validator, naming the field")]
    public async Task StreakThreshold_101_Returns400() =>
        await ShouldBeValidationErrorFor(
            await CreateAsync(BasePayload("thresh-101", streakThreshold: 101)), "streakThreshold");

    [Fact(DisplayName = "StreakGraceDays == LengthInDays is rejected by the validator (max is LengthInDays - 1)")]
    public async Task StreakGraceDays_EqualToLengthInDays_Returns400() =>
        await ShouldBeValidationErrorFor(
            await CreateAsync(BasePayload("grace-eq-length", lengthInDays: 7, streakGraceDays: 7)), "streakGraceDays");

    [Fact(DisplayName = "ReminderLeadDays on a one-day period is rejected by the validator (no valid lead exists)")]
    public async Task ReminderLeadDays_OnOneDayPeriod_Returns400() =>
        await ShouldBeValidationErrorFor(
            await CreateAsync(BasePayload("lead-one-day", lengthInDays: 1, resetAnchorDay: 0, reminderLeadDays: 1)),
            "reminderLeadDays");

    [Fact(DisplayName = "ResetAnchorDay == 8 on a weekly-aligned period (length 7) is rejected by the validator")]
    public async Task ResetAnchorDay_8_OnWeeklyAlignedPeriod_Returns400() =>
        await ShouldBeValidationErrorFor(
            await CreateAsync(BasePayload("anchor-8-weekly", lengthInDays: 7, resetAnchorDay: 8)), "resetAnchorDay");

    [Fact(DisplayName = "ResetAnchorDay == 31 on a non-weekly period (length 10) is rejected by the validator")]
    public async Task ResetAnchorDay_31_OnNonWeeklyPeriod_Returns400() =>
        await ShouldBeValidationErrorFor(
            await CreateAsync(BasePayload("anchor-31-nonweekly", lengthInDays: 10, resetAnchorDay: 31)), "resetAnchorDay");

    [Fact(DisplayName = "HistoryDepth == 0 is rejected by the validator, naming the field")]
    public async Task HistoryDepth_Zero_Returns400() =>
        await ShouldBeValidationErrorFor(
            await CreateAsync(BasePayload("history-zero", historyDepth: 0)), "historyDepth");

    [Fact(DisplayName = "HistoryDepth == 101 is rejected by the validator, naming the field")]
    public async Task HistoryDepth_101_Returns400() =>
        await ShouldBeValidationErrorFor(
            await CreateAsync(BasePayload("history-101", historyDepth: 101)), "historyDepth");

    /// <summary>
    /// The other half of the pin: the two rules added for <c>CQ-3</c> must reject one past the bound and not
    /// the bound itself. Without these an <c>ExclusiveBetween</c> — or a copied 1–100 on a field whose range is
    /// 1–365 — would pass every case above.
    /// </summary>
    [Theory(DisplayName = "The top of each range is accepted")]
    [InlineData(365, 16)]
    [InlineData(7, 100)]
    public async Task TopOfRange_IsAccepted(int lengthInDays, int historyDepth)
    {
        var response = await CreateAsync(BasePayload($"top-{lengthInDays}-{historyDepth}",
            lengthInDays: lengthInDays, historyDepth: historyDepth));

        response.StatusCode.Should().Be(HttpStatusCode.Created,
            await response.Content.ReadAsStringAsync(CancellationToken));
    }
}
