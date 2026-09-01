using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// TEST-6 / `CQ-2` — a <c>TimeDto</c> that arrives out of range is a 400 naming the field, never a 500.
///
/// <para>The mechanism this pins is a silent one. <c>TimeDto</c> is only ever a <b>member</b> of a request,
/// so a <c>Validator&lt;TimeDto&gt;</c> is never resolved by FastEndpoints and never runs — the repo carried
/// exactly that dead validator, and every request validator either re-implemented the range check inline or
/// (History, Tracking) simply had none. The endpoint then calls <c>TimeDto.ToTimeOnly()</c>, which is
/// <c>new TimeOnly(Hours, Minutes)</c> and throws <see cref="ArgumentOutOfRangeException"/>, so the gap
/// surfaces as an unhandled 500 rather than a validation error.</para>
///
/// <para>Both halves matter, hence the second theory: a fix applied to one of two sibling properties is the
/// likely half-fix, and a range rule that fires but breaks the 200 case is the likely over-fix.</para>
/// </summary>
[Collection("Postgres")]
public class TimeDtoRangeValidationTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const string StackedBarsRoute = "/api/activity-history/dashboard/summary/stacked-bars";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Theory(DisplayName = "CQ-2: an out-of-range TimeDto is a 400 naming the field, not a 500")]
    [InlineData("windowStartTime", 24, 0)]
    [InlineData("windowStartTime", 99, 0)]
    [InlineData("windowStartTime", -1, 0)]
    [InlineData("windowStartTime", 0, 60)]
    [InlineData("windowStartTime", 0, -1)]
    [InlineData("windowEndTime", 99, 0)]
    [InlineData("windowEndTime", 0, 60)]
    public async Task StackedBars_OutOfRangeWindowTime_IsRejected(string field, int hours, int minutes)
    {
        var response = await PostStackedBarsAsync(
            startTime: field == "windowStartTime" ? (hours, minutes) : (0, 0),
            endTime: field == "windowEndTime" ? (hours, minutes) : (0, 0));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "ToTimeOnly() throws on this value, so without a validation rule the request is a 500");

        // Asserting the body too: a bare "not 200" would pass on the 500 this test exists to rule out.
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json, CancellationToken);
        body.GetProperty("errors").GetRawText()
            .Should().Contain(field, "the client needs to know which of the two window ends it got wrong");
    }

    [Theory(DisplayName = "CQ-2: the boundaries of a valid time of day are accepted")]
    [InlineData(0, 0)]
    [InlineData(23, 59)]
    public async Task StackedBars_BoundaryWindowTimes_AreAccepted(int hours, int minutes)
    {
        var response = await PostStackedBarsAsync((hours, minutes), (hours, minutes));

        response.StatusCode.Should().Be(HttpStatusCode.OK, "23:59 and 00:00 are both real times of day");
    }

    /// <summary>
    /// The regression pin for the refactor on the Planning side: those validators hand-rolled the same
    /// range check, and folding them onto the shared rule has to keep the 400 they already produced.
    ///
    /// <para>No task is seeded on purpose. Validation runs before the handler, so the invalid case is a 400
    /// whatever the id refers to, and the valid case falling through to a 404 is what proves the rule
    /// accepted the time rather than the request dying earlier for some other reason.</para>
    /// </summary>
    [Theory(DisplayName = "CQ-2: Planning's inline range checks survive the move to the shared rule")]
    [InlineData(99, 0, HttpStatusCode.BadRequest)]
    [InlineData(23, 59, HttpStatusCode.NotFound)]
    public async Task PatchPlannerTaskStatus_ValidatesActualStartTime(
        int hours, int minutes, HttpStatusCode expected)
    {
        var response = await CreateClient().PatchAsJsonAsync(
            "api/planner-task/999999999/status",
            new { Status = "InProgress", ActualStartTime = new { Hours = hours, Minutes = minutes } },
            JsonOpts,
            CancellationToken);

        response.StatusCode.Should().Be(expected);
    }

    private Task<HttpResponseMessage> PostStackedBarsAsync(
        (int Hours, int Minutes) startTime, (int Hours, int Minutes) endTime) =>
        CreateUserRoleClient().PostAsJsonAsync(StackedBarsRoute, new
        {
            date = new DateOnly(2026, 3, 1),
            rangeType = "Week",
            groupBy = "Activity",
            windowMinutes = 1440,
            windowStartTime = new { hours = startTime.Hours, minutes = startTime.Minutes },
            windowEndTime = new { hours = endTime.Hours, minutes = endTime.Minutes }
        }, Json, CancellationToken);
}
