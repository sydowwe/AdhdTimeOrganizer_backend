using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.domain.valueObject;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// Archiving an activity retires it from the pickers; it does not erase the time already logged against it.
/// These pin that on every history read that reaches the activity.
/// </summary>
/// <remarks>
/// <para>
/// <b>The failure this exists to stop.</b> <c>ActivityHistory</c> carries no archived filter and
/// <c>Activity</c> does, and the history reads reach the activity through a <b>required</b> navigation — by
/// projection in the detail list, by <c>Include</c> in the calendar and all six dashboards. A filtered
/// principal on a required navigation drops the row rather than blanking its fields, so those surfaces
/// silently stopped counting the archived activity's sessions while the surfaces that never touch
/// <c>Activity</c> — time-of-day, aggregate-by-activity, and the summary cards' own previous-period total —
/// went on counting them. Two charts on one page, one range, different totals, 200 on both.
/// </para>
/// <para>
/// So the assertions here are <b>cross-checks between surfaces</b>, not just "the archived row is present":
/// the pie has to agree with the time-of-day chart beside it. A test that only read one endpoint would pass
/// against any consistent-but-wrong rule, and this bug was never a crash — only a number that was too small.
/// </para>
/// </remarks>
[Collection("Postgres")]
public class ArchivedActivityHistoryReadTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private static long UserId => FakeLoggedUserService.TestUserId;

    private const string ListRoute = "api/activity-history/filter";
    private const string CalendarRoute = "api/activity-history/dashboard/calendar";
    private const string DetailPieRoute = "api/activity-history/dashboard/detail/pie-chart";
    private const string DetailCardsRoute = "api/activity-history/dashboard/detail/summary-cards";
    private const string SummaryPieRoute = "api/activity-history/dashboard/summary/pie-chart";
    private const string SummaryCardsRoute = "api/activity-history/dashboard/summary/summary-cards";
    private const string TimeOfDayRoute = "api/activity-history/dashboard/summary/time-of-day";

    private static string Today => DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
    private static object Midnight => new { hours = 0, minutes = 0 };

    /// <summary>The whole of the user's today, which is the window both seeded sessions fall in.</summary>
    private static object DetailBody => new { date = Today, from = Midnight, to = Midnight, groupBy = "Activity" };

    private static object SummaryBody => new { date = Today, rangeType = "Week", groupBy = "Activity" };

    /// <summary>
    /// Two identical half-hour sessions, one on an activity archived afterwards and one on a live activity.
    /// Identical because every assertion below is "the archived one is counted exactly like the live one" —
    /// equal lengths turn that into a single number to check rather than two to keep in step.
    /// </summary>
    private async Task<(long ArchivedActivityId, long ArchivedHistoryId)> SeedArchivedAndLiveSessionsAsync(DbContext db)
    {
        var start = DateTime.UtcNow.Date.AddHours(10);

        var archivedActivityId = await ActivityProfileTestSeedHelper.SeedActivityAsync(db, UserId, "Retired Hobby");
        var liveActivityId = await ActivityProfileTestSeedHelper.SeedActivityAsync(db, UserId, "Still Doing It");

        long archivedHistoryId = 0;

        foreach (var activityId in new[] { archivedActivityId, liveActivityId })
        {
            var row = new ActivityHistory
            {
                UserId = UserId,
                ActivityId = activityId,
                StartTimestamp = start,
                Length = new IntTime(0, 30),
                EndTimestamp = start.AddMinutes(30)
            };
            db.Set<ActivityHistory>().Add(row);
            await db.SaveChangesAsync(CancellationToken);

            if (activityId == archivedActivityId)
                archivedHistoryId = row.Id;
        }

        // Archived after the sessions exist, which is the order it happens in: you log the time, then retire
        // the activity. Archiving first would have been a different (and much more visible) test.
        var activity = await db.Set<Activity>().SingleAsync(a => a.Id == archivedActivityId, CancellationToken);
        activity.IsArchived = true;
        await db.SaveChangesAsync(CancellationToken);

        return (archivedActivityId, archivedHistoryId);
    }

    private async Task<JsonElement> PostAsync(string route, object body)
    {
        var response = await CreateClient().PostAsJsonAsync(route, body, JsonOpts, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts, CancellationToken);
    }

    // ---- the detail list and its edit form -------------------------------------------------------

    [Fact]
    public async Task HistoryList_KeepsTheSessionsOfAnArchivedActivity()
    {
        await using var db = CreateDbContext();
        await SeedArchivedAndLiveSessionsAsync(db);

        var rows = await PostAsync(ListRoute, new { date = Today, from = Midnight, to = Midnight });

        var names = rows.EnumerateArray()
            .Select(row => row.GetProperty("activity").GetProperty("name").GetString())
            .ToArray();

        names.Should().BeEquivalentTo(["Retired Hobby", "Still Doing It"],
            "the list is a record of time spent, and the response projects the activity through a required "
            + "navigation — so without the archived lift the retired activity's session left the list while "
            + "the charts above it went on counting it");

        var archivedRow = rows.EnumerateArray()
            .Single(row => row.GetProperty("activity").GetProperty("name").GetString() == "Retired Hobby");
        archivedRow.GetProperty("activity").GetProperty("isArchived").GetBoolean().Should().BeTrue(
            "the row carries the fact, so the client can mark it as belonging to something retired");
    }

    [Fact]
    public async Task GetByIdHistory_OpensASessionOfAnArchivedActivity()
    {
        await using var db = CreateDbContext();
        var (_, archivedHistoryId) = await SeedArchivedAndLiveSessionsAsync(db);

        var response = await CreateClient().GetAsync($"api/activity-history/{archivedHistoryId}", CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "the row exists and belongs to the caller — the 404 was the projection's join, not a missing row, "
            + "and it fell on exactly the rows the list offers");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts, CancellationToken);
        body.GetProperty("activity").GetProperty("name").GetString().Should().Be("Retired Hobby");
    }

    // ---- the charts, cross-checked against each other ---------------------------------------------

    /// <summary>
    /// The pin that would have caught this. <c>time-of-day</c> never joins <c>Activity</c>, so it always
    /// counted both sessions; the grouped charts did not. Asserting the grouped totals <em>against</em> it
    /// ties the two together, so a future read that quietly drops filtered rows again fails here rather than
    /// showing a smaller number.
    /// </summary>
    [Theory]
    [InlineData(DetailPieRoute, false)]
    [InlineData(SummaryPieRoute, true)]
    public async Task PieChart_CountsTheArchivedActivitysSessions_AndAgreesWithTimeOfDay(string route, bool isSummary)
    {
        await using var db = CreateDbContext();
        await SeedArchivedAndLiveSessionsAsync(db);

        var timeOfDay = await PostAsync(TimeOfDayRoute, new { date = Today, rangeType = "Week" });
        var timeOfDayTotal = timeOfDay.GetProperty("hours").EnumerateArray()
            .Sum(hour => hour.GetProperty("totalSeconds").GetInt64());

        timeOfDayTotal.Should().Be(3600, "two half-hour sessions, and this chart never touched Activity");

        var pie = await PostAsync(route, isSummary ? SummaryBody : DetailBody);

        pie.GetProperty("totals").GetProperty("totalSeconds").GetInt64().Should().Be(timeOfDayTotal,
            "one range cannot have two totals depending on which chart of the same page asked");
        pie.GetProperty("totals").GetProperty("uniqueGroups").GetInt32().Should().Be(2);

        pie.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("name").GetString())
            .Should().Contain("Retired Hobby");
    }

    /// <summary>
    /// Summary cards carry the same hole twice over: the current and baseline reads joined <c>Activity</c>
    /// and the previous-period total never did, so <c>percentChange</c> compared a period counting archived
    /// rows against one that did not — a wrong number with nothing to flag it.
    /// </summary>
    [Theory]
    [InlineData(DetailCardsRoute, false)]
    [InlineData(SummaryCardsRoute, true)]
    public async Task SummaryCards_CountTheArchivedActivitysSessions(string route, bool isSummary)
    {
        await using var db = CreateDbContext();
        await SeedArchivedAndLiveSessionsAsync(db);

        var cards = await PostAsync(route, isSummary ? SummaryBody : DetailBody);

        cards.GetProperty("periodComparison").GetProperty("currentPeriodTotalSeconds").GetInt64().Should().Be(3600,
            "the current period has to be measured by the same rule as the period it is compared against");

        cards.GetProperty("cards").EnumerateArray()
            .Select(card => card.GetProperty("name").GetString())
            .Should().Contain("Retired Hobby");
    }

    /// <summary>
    /// The calendar joins the user's days against their history, so a day whose only session was logged
    /// against a since-archived activity read as an empty day — and its totals have to match the charts.
    /// </summary>
    [Fact]
    public async Task Calendar_CountsTheArchivedActivitysSessionsOnTheDay()
    {
        await using var db = CreateDbContext();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await PlanningTestSeedHelper.SeedCalendarAsync(db, today, UserId, CancellationToken);

        await SeedArchivedAndLiveSessionsAsync(db);

        var days = await PostAsync(CalendarRoute, new { startDate = Today, endDate = Today });

        var day = days.EnumerateArray().Should().ContainSingle().Subject;
        day.GetProperty("totalSeconds").GetInt64().Should().Be(3600);
        day.GetProperty("sessionCount").GetInt32().Should().Be(2,
            "a day of the calendar cannot lose a session because the activity was retired afterwards");
    }
}
