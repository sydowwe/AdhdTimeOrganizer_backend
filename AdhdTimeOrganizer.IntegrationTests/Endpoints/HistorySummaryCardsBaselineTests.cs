using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.History.application.dashboard;
using AdhdTimeOrganizer.History.application.dto.@enum;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.domain.valueObject;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// Drives every <c>baseline</c> the summary-cards requests advertise, on both routes that take one.
///
/// <para>Nothing else does. Both endpoints defaulted to <c>last7days</c> in every test that touched them, and
/// the <c>allTime</c> arm resolved its lower bound to <c>DateTime.MinValue</c> — an <c>Unspecified</c>-kind
/// <see cref="DateTime"/> that Npgsql refuses to write to <c>activity_history.start_timestamp</c>, a
/// <c>timestamptz</c> column, so the route threw a 500 for a value its own DTO documents. The bound is now
/// omitted entirely for <c>allTime</c> (<c>HistoryBaselineWindow.ApplyRange</c>).</para>
///
/// <para>The two round-trip theories cannot see that defect and never could — the shared Postgres fixture
/// turns <c>Npgsql.EnableLegacyTimestampBehavior</c> on for every integration test in the solution and the
/// host does not, so a bound that throws in production is accepted here. They pin the range's <i>meaning</i>,
/// which is a separate thing worth pinning; <see cref="ResolvedBaseline_HasNoLowerBoundThatIsNotUtc"/> is the
/// one that pins the kind, and it deliberately asserts on the resolver instead of over the wire.</para>
/// </summary>
[Collection("Postgres")]
public class HistorySummaryCardsBaselineTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    private const string DetailRoute = "/api/activity-history/dashboard/detail/summary-cards";
    private const string SummaryRoute = "/api/activity-history/dashboard/summary/summary-cards";

    /// <summary>The reported day. Both routes are pointed at it; they differ only in how they bound it.</summary>
    private static readonly DateOnly ReportedDay = new(2026, 3, 9);

    /// <summary>
    /// Two months before <see cref="ReportedDay"/> — outside every bounded baseline (7 days, 30 days, and the
    /// 56 <c>sameWeekday</c> looks back over), so only <c>allTime</c> can see it.
    /// </summary>
    private static readonly DateTime HistoricRowStart = new(2026, 1, 5, 9, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime CurrentRowStart = new(2026, 3, 9, 9, 0, 0, DateTimeKind.Utc);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Every baseline the request DTO accepts is answerable. <c>allTime</c> is the one that was not: it is
    /// the only arm whose lower bound was not derived from the current period's UTC-kinded start.
    /// </summary>
    [Theory]
    [InlineData(DetailRoute, "last7days")]
    [InlineData(DetailRoute, "last30days")]
    [InlineData(DetailRoute, "sameWeekday")]
    [InlineData(DetailRoute, "allTime")]
    [InlineData(SummaryRoute, "last7days")]
    [InlineData(SummaryRoute, "last30days")]
    [InlineData(SummaryRoute, "sameWeekday")]
    [InlineData(SummaryRoute, "allTime")]
    public async Task EveryAdvertisedBaseline_IsAnswered(string route, string baseline)
    {
        await SeedTheTwoRowsAsync();

        var response = await PostRawAsync(route, BodyFor(route, baseline));

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "baseline is a documented request field, so a value the DTO advertises may not 500");
    }

    /// <summary>
    /// The other half of <c>CQ-7</c>, and the reason <c>Baseline</c> stopped being a <c>string</c>: a value
    /// nobody advertises must be <b>refused</b>, not quietly answered as <c>last7days</c>. A typo used to
    /// come back 200 carrying a baseline the caller never asked for — a wrong number that looks right, which
    /// is the failure mode this slice is supposed to design against.
    ///
    /// <para>Surrounding whitespace is <b>not</b> in here on purpose: <c>Enum.TryParse</c>, which the
    /// converter uses, trims it, so <c>"last30Days "</c> resolves to the member the caller plainly meant.
    /// That is leniency, not a wrong answer — the hazard was only ever a value with no member behind it.</para>
    /// </summary>
    [Theory]
    [InlineData(DetailRoute, "somethingTheClientMadeUp")]
    [InlineData(DetailRoute, "last_30_days")]
    [InlineData(SummaryRoute, "somethingTheClientMadeUp")]
    [InlineData(SummaryRoute, "last_30_days")]
    public async Task UnknownBaseline_IsRefused(string route, string baseline)
    {
        await SeedTheTwoRowsAsync();

        var response = await PostRawAsync(route, BodyFor(route, baseline));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "an unrecognised baseline must be an error the caller can see, not a silent fallback to last7days");
    }

    /// <summary>
    /// <c>allTime</c> means all time: a row two months old is in its baseline, and in no other one. Asserted
    /// through <c>isNew</c> / <c>averageSeconds</c> because those are the only two fields on the wire that
    /// change when the baseline range does — a card whose baseline found nothing reads as new.
    /// </summary>
    [Theory]
    [InlineData(DetailRoute)]
    [InlineData(SummaryRoute)]
    public async Task AllTime_ReadsRowsOlderThanEveryBoundedBaseline(string route)
    {
        await SeedTheTwoRowsAsync();

        var bounded = await CardOf(route, "last30days");

        bounded.GetProperty("isNew").GetBoolean().Should().BeTrue(
            "the January row is two months back, so a 30-day baseline finds nothing to compare against");
        bounded.GetProperty("averageSeconds").GetInt64().Should().Be(0);

        var allTime = await CardOf(route, "allTime");

        allTime.GetProperty("isNew").GetBoolean().Should().BeFalse(
            "allTime reaches the January row -- an empty baseline here would mean the lower bound was still " +
            "being applied, just with a value that no longer throws");
        allTime.GetProperty("averageSeconds").GetInt64().Should().BeGreaterThan(0);
    }

    /// <summary>
    /// <c>averageSeconds</c> is the baseline on the card's own scale: its mean day multiplied out to the
    /// reported period, so it reads beside <c>totalSeconds</c> as "usually". It used to be the bare daily
    /// mean, which on any multi-day range sat next to a period total and made the client's "+40%" look
    /// like it was computed against a number a quarter of the size.
    /// </summary>
    [Fact]
    public async Task AverageSeconds_IsTheBaselineScaledToTheReportedPeriod()
    {
        await using (var db = CreateDbContext())
        {
            var role = new ActivityRole { UserId = UserId, Name = "Scale role", Color = "#334455" };
            db.Set<ActivityRole>().Add(role);
            await db.SaveChangesAsync(CancellationToken);

            var activity = new Activity { UserId = UserId, Name = "Scale", RoleId = role.Id };
            db.Set<Activity>().Add(activity);
            await db.SaveChangesAsync(CancellationToken);

            db.Set<ActivityHistory>().AddRange(
                // Inside the last7days baseline of a ThreeDays range over 03-09 (03-06..03-09).
                NewRow(activity.Id, new DateTime(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc), new IntTime(7, 0)),
                NewRow(activity.Id, CurrentRowStart, new IntTime(2, 0)));
            await db.SaveChangesAsync(CancellationToken);
        }

        var card = await CardOf(SummaryRoute, "last7days");

        // 7h over 7 baseline days is 1h a day; the reported range spans 4 days, so a usual one holds 4h.
        card.GetProperty("averageSeconds").GetInt64().Should().Be(4 * 3600);
        card.GetProperty("totalSeconds").GetInt64().Should().Be(2 * 3600);
        card.GetProperty("percentChange").GetDouble().Should().Be(-50.0,
            "the percentage is computed against the same number the card now reports as its baseline");
    }

    /// <summary>
    /// The guard for the sentinel itself, asserted on the resolver rather than over the wire — because the
    /// wire cannot see it here. <see cref="Sydowwe.Framework.Testing.PostgresContainerFixture{TProgram,
    /// TDbContext}"/> sets <c>Npgsql.EnableLegacyTimestampBehavior</c> for every integration test in the
    /// solution, and the host does not, so an <c>Unspecified</c>-kind bound that throws in production is
    /// accepted by every test above. Any bound reaching a <c>timestamptz</c> parameter must therefore be
    /// UTC-kinded or absent, and this is what says so.
    /// </summary>
    [Theory]
    [InlineData(HistoryBaseline.Last7Days)]
    [InlineData(HistoryBaseline.Last30Days)]
    [InlineData(HistoryBaseline.SameWeekday)]
    [InlineData(HistoryBaseline.AllTime)]
    public void ResolvedBaseline_HasNoLowerBoundThatIsNotUtc(HistoryBaseline baseline)
    {
        var currentFrom = new DateTime(2026, 3, 9, 0, 0, 0, DateTimeKind.Utc);

        var window = HistoryBaselineWindow.Resolve(
            currentFrom, ReportedDay, ReportedDay, baseline);

        if (window.From is { } from)
            from.Kind.Should().Be(DateTimeKind.Utc,
                "Npgsql refuses any non-UTC DateTime for a timestamptz column, so a sentinel lower bound " +
                "is a 500 on a baseline value the request DTO advertises -- omit the bound instead");

        window.To.Kind.Should().Be(DateTimeKind.Utc);
    }

    // ---- helpers ---------------------------------------------------------

    /// <summary>One row inside the reported day, one two months before it. Both on the same activity, so the
    /// single card the response carries compares the second against the first.</summary>
    private async Task SeedTheTwoRowsAsync()
    {
        await using var db = CreateDbContext();

        var role = new ActivityRole { UserId = UserId, Name = "Baseline role", Color = "#334455" };
        db.Set<ActivityRole>().Add(role);
        await db.SaveChangesAsync(CancellationToken);

        var activity = new Activity { UserId = UserId, Name = "Baseline", RoleId = role.Id };
        db.Set<Activity>().Add(activity);
        await db.SaveChangesAsync(CancellationToken);

        db.Set<ActivityHistory>().AddRange(
            NewRow(activity.Id, HistoricRowStart, new IntTime(5, 0)),
            NewRow(activity.Id, CurrentRowStart, new IntTime(2, 0)));

        await db.SaveChangesAsync(CancellationToken);
    }

    private static ActivityHistory NewRow(long activityId, DateTime startUtc, IntTime length) =>
        new()
        {
            UserId = UserId,
            ActivityId = activityId,
            StartTimestamp = startUtc,
            EndTimestamp = startUtc.AddSeconds(length.TotalSeconds),
            Length = length
        };

    /// <summary>
    /// <c>detail/</c> takes a date plus a wall-clock window — an equal from/to spans the whole day — while
    /// <c>summary/</c> takes a named range type. That difference is the only thing separating the two
    /// endpoints; the baseline is resolved identically from there.
    /// </summary>
    private static object BodyFor(string route, string baseline) => route == DetailRoute
        ? new
        {
            date = ReportedDay,
            from = new { hours = 0, minutes = 0 },
            to = new { hours = 0, minutes = 0 },
            groupBy = "Activity",
            baseline
        }
        : new { date = ReportedDay, rangeType = "ThreeDays", groupBy = "Activity", baseline };

    private async Task<JsonElement> CardOf(string route, string baseline)
    {
        var response = await PostRawAsync(route, BodyFor(route, baseline));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json, CancellationToken);
        var cards = body.GetProperty("cards").EnumerateArray().ToList();

        cards.Should().ContainSingle("only one activity is seeded, and it has time inside the reported day");
        return cards[0];
    }

    private Task<HttpResponseMessage> PostRawAsync(string route, object body) =>
        CreateUserRoleClient().PostAsJsonAsync(route, body, Json, CancellationToken);
}
