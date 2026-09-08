using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner.template;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// TEST-8 / <c>CQ-12</c> — the midnight-wrap time window, which is the day planner's only read filter and the
/// template editor's. The predicate used to exist twice, character-for-character, over two different entities;
/// it now lives once on <c>TaskPlannerHelper.InTimeWindow</c> and every case below runs against
/// <b>both</b> endpoints, so the two can never answer the same window differently again.
/// </summary>
/// <remarks>
/// <para>
/// Two asymmetries the table encodes deliberately, because they are the whole behaviour of the predicate:
/// </para>
/// <list type="bullet">
/// <item>
/// <b>The wrapping branch is inclusive at both edges; the standard branch is strict.</b> A window
/// <c>13:00 → 08:00</c> therefore matches an <c>08:00–09:00</c> task (it touches the window's far edge),
/// while <c>09:00 → 10:00</c> does not match an <c>08:00–09:00</c> one. That is why <c>00:00 → 00:00</c>
/// means "the whole day" — every task's end is <c>&gt;= 00:00</c> — which is the window the home page sends.
/// </item>
/// <item>
/// <b>A task that itself wraps midnight matches every window</b>, via the third clause of each branch. No
/// write path accepts such a row any more (<c>CQ-14</c>), but there is no CHECK constraint and older rows
/// may still carry it, so <c>F</c> below is seeded directly and appears in every expectation — including
/// <c>03:00 → 04:00</c>, which is the case that proves the clause is doing something.
/// </item>
/// </list>
/// </remarks>
[Collection("Postgres")]
public class PlannerTaskFilterWindowTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    /// <summary>One day's tasks, and the same six spans as template lines. <c>F</c> wraps midnight.</summary>
    private static readonly (string Label, TimeOnly Start, TimeOnly End)[] Spans =
    [
        ("A", new TimeOnly(0, 0), new TimeOnly(1, 0)),
        ("B", new TimeOnly(8, 0), new TimeOnly(9, 0)),
        ("C", new TimeOnly(9, 0), new TimeOnly(10, 0)),
        ("D", new TimeOnly(12, 0), new TimeOnly(13, 0)),
        ("E", new TimeOnly(22, 0), new TimeOnly(23, 0)),
        ("F", new TimeOnly(23, 0), new TimeOnly(1, 0))
    ];

    public static TheoryData<int, int, int, int, string> Windows => new()
    {
        // fromH, fromM, untilH, untilM, expected labels in StartTime order
        { 8, 0, 14, 0, "B,C,D,F" },
        { 9, 0, 10, 0, "C,F" },       // strict: B ends exactly at 09:00 and D starts exactly at 13:00
        { 9, 30, 9, 45, "C,F" },      // a window wholly inside one task
        { 22, 0, 2, 0, "A,E,F" },     // wraps
        { 0, 0, 0, 0, "A,B,C,D,E,F" },// wraps -> the whole day
        { 13, 0, 8, 0, "A,B,D,E,F" }, // wraps, and inclusive: B touches the 08:00 edge, D touches the 13:00 one
        { 3, 0, 4, 0, "F" }           // only the midnight-wrapping task
    };

    [Theory]
    [MemberData(nameof(Windows))]
    public async Task PlannerTaskFilter_MatchesTheWindow(int fromH, int fromM, int untilH, int untilM, string expected)
    {
        var calendarId = await SeedDayAsync();

        var tasks = await FilterAsync<PlannerTaskResponse>("api/planner-task/filter", new
        {
            CalendarId = calendarId,
            From = new { Hours = fromH, Minutes = fromM },
            Until = new { Hours = untilH, Minutes = untilM }
        });

        LabelsOf(tasks.Select(t => (t.StartTime.Hours, t.StartTime.Minutes, t.EndTime.Hours, t.EndTime.Minutes)))
            .Should().Be(expected);
    }

    [Theory]
    [MemberData(nameof(Windows))]
    public async Task TemplatePlannerTaskFilter_MatchesTheSameWindow(int fromH, int fromM, int untilH, int untilM, string expected)
    {
        var templateId = await SeedTemplateAsync();

        var tasks = await FilterAsync<TemplatePlannerTaskResponse>("api/template-planner-task/filter", new
        {
            TemplateId = templateId,
            From = new { Hours = fromH, Minutes = fromM },
            Until = new { Hours = untilH, Minutes = untilM }
        });

        LabelsOf(tasks.Select(t => (t.StartTime.Hours, t.StartTime.Minutes, t.EndTime.Hours, t.EndTime.Minutes)))
            .Should().Be(expected, "the template editor must read the same window as the day planner");
    }

    /// <summary>
    /// <c>AlwaysSortBy</c> is developer-supplied — the filter request carries no sort of its own — and is
    /// applied to the entity query before projection. The day planner draws the returned order straight down
    /// the column, so it is behaviour, not incidental. The whole-day window is the one that shows it on all
    /// six rows.
    /// </summary>
    [Fact(DisplayName = "Results come back in StartTime order")]
    public async Task Results_AreAlwaysSortedByStartTime()
    {
        var calendarId = await SeedDayAsync();

        var tasks = await FilterAsync<PlannerTaskResponse>("api/planner-task/filter", new
        {
            CalendarId = calendarId,
            From = new { Hours = 0, Minutes = 0 },
            Until = new { Hours = 0, Minutes = 0 }
        });

        tasks.Select(t => new TimeOnly(t.StartTime.Hours, t.StartTime.Minutes)).Should().BeInAscendingOrder();
    }

    /// <summary>
    /// The endpoint has no ownership check of its own — it relies entirely on the global user query filter, and
    /// nothing else pins that for this route. A foreign calendar id must read as an empty day, not as a 403 and
    /// certainly not as that user's tasks.
    /// </summary>
    [Fact(DisplayName = "Another user's calendar id filters to nothing, not to their tasks")]
    public async Task ForeignCalendarId_ReturnsNothing()
    {
        var foreignUserId = await ActivityMergeTestSupport.SecondUserAsync(
            Fixture, $"filter-window-foreign-{Guid.NewGuid():N}@test.com");

        long foreignCalendarId;
        await using (var db = CreateDbContext())
        {
            var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "filter-foreign", foreignUserId, CancellationToken);
            foreignCalendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, new DateOnly(2028, 4, 2), foreignUserId, CancellationToken);
            foreach (var (_, start, end) in Spans)
                await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, foreignCalendarId, start, end, foreignUserId, ct: CancellationToken);
        }

        var tasks = await FilterAsync<PlannerTaskResponse>("api/planner-task/filter", new
        {
            CalendarId = foreignCalendarId,
            From = new { Hours = 0, Minutes = 0 },
            Until = new { Hours = 0, Minutes = 0 }
        });

        tasks.Should().BeEmpty();
    }

    // ─── helpers ─────────────────────────────────────────────────────────────

    private async Task<long> SeedDayAsync()
    {
        await using var db = CreateDbContext();
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "filter-window", UserId, CancellationToken);
        var calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, new DateOnly(2028, 4, 1), UserId, CancellationToken);

        // Seeded directly, not posted: PlannerTaskValidator rejects F's EndTime < StartTime at the door.
        foreach (var (_, start, end) in Spans)
            await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, calendarId, start, end, UserId, ct: CancellationToken);

        return calendarId;
    }

    private async Task<long> SeedTemplateAsync()
    {
        await using var db = CreateDbContext();
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "filter-window-template", UserId, CancellationToken);
        var templateId = await PlanningTestSeedHelper.SeedTaskPlannerDayTemplateAsync(db, UserId, ct: CancellationToken);

        foreach (var (_, start, end) in Spans)
            await PlanningTestSeedHelper.SeedTemplatePlannerTaskAsync(db, templateId, activityId, start, end, UserId, CancellationToken);

        return templateId;
    }

    private async Task<List<T>> FilterAsync<T>(string route, object body)
    {
        var response = await CreateClient().PostAsJsonAsync(route, body, JsonOpts, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<List<T>>(JsonOpts, CancellationToken))!;
    }

    /// <summary>Names the returned spans back as fixture labels, so a failure reads "B,C,D,F" not four tuples.</summary>
    private static string LabelsOf(IEnumerable<(int StartH, int StartM, int EndH, int EndM)> spans) =>
        string.Join(',', spans.Select(s =>
            Spans.Single(f => f.Start == new TimeOnly(s.StartH, s.StartM) && f.End == new TimeOnly(s.EndH, s.EndM)).Label));
}
