using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.Planning.application.helper;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// The cross-day plan-vs-actual trend, end to end over <c>POST /planner-task/plan-vs-actual-trend</c>.
/// <para>
/// The arithmetic is pinned without a database in <see cref="Services.PlanVsActualTrendServiceTests"/>. What
/// is left for this file is the half that lives in the <b>query</b> and fails silently: the eligibility
/// predicate (background, cancelled, and days that are not over yet all have to leave the population before
/// the rules ever see a row), the user scoping — this slice compiles against a plain <c>DbContext</c> and so
/// cannot see the host's global filter — and the empty answer, which is six zeroes rather than a 404 because
/// the client is meant to render nothing.
/// </para>
/// <para>
/// The caller is pinned to <b>UTC</b> so "today" is the same day the test computes. The day boundary itself
/// is the user's own zone, exactly as the streak takes it; that convention has its own coverage in
/// <see cref="HistoryDashboardUserZoneTests"/> and is not re-litigated here.
/// </para>
/// </summary>
[Collection("Postgres")]
public class PlanVsActualTrendTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    /// <summary>Runs after the fixture's baseline seed. UTC is what that seed already uses; this pins it.</summary>
    protected override async Task SeedAsync(DbContext db)
    {
        var user = await db.Set<User>().IgnoreQueryFilters().SingleAsync(u => u.Id == UserId);
        user.Timezone = TimeZoneInfo.Utc;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// The headline case, across two days so it cannot be answered by anything the day view already holds:
    /// four hours planned, five logged, with the late and long counts that make the drift legible.
    /// </summary>
    [Fact]
    public async Task Trend_SumsPlannedAgainstLogged_AcrossDays()
    {
        var activityId = await SeedActivityAsync("trend-basic");

        // Yesterday: an hour planned, ninety minutes logged, started fifteen minutes late.
        await SeedTaskAsync(activityId, Today.AddDays(-1), PlannerTaskStatus.Completed,
            planned: ("09:00", "10:00"), actual: ("09:15", "10:45"));
        // Yesterday: three hours planned, run to the minute.
        await SeedTaskAsync(activityId, Today.AddDays(-1), PlannerTaskStatus.Completed,
            planned: ("13:00", "16:00"), actual: ("13:00", "16:00"));
        // The day before: an hour and a half planned, started early and finished early.
        await SeedTaskAsync(activityId, Today.AddDays(-2), PlannerTaskStatus.Completed,
            planned: ("08:00", "09:30"), actual: ("07:45", "08:45"));

        var trend = await GetTrendAsync(Today.AddDays(-7), Today);

        trend.TaskCount.Should().Be(3);
        trend.PlannedMinutes.Should().Be(60 + 180 + 90);
        trend.ActualMinutes.Should().Be(90 + 180 + 60);
        trend.StartedLateCount.Should().Be(1);
        trend.RanLongerCount.Should().Be(1);
        trend.NeverHappenedCount.Should().Be(0);
    }

    /// <summary>
    /// <b>Today never counts, however late in the day the request arrives.</b> Its unstarted tasks have not
    /// failed at anything — they have not come around yet — and every one of them would land in
    /// <c>NeverHappenedCount</c> and drag the whole range with it. A range that ends today therefore answers
    /// about the days before it, and a range that <i>is</i> today answers with nothing at all.
    /// </summary>
    [Fact]
    public async Task Trend_ExcludesToday_HoweverLateItIs()
    {
        var activityId = await SeedActivityAsync("trend-today");

        await SeedTaskAsync(activityId, Today.AddDays(-1), PlannerTaskStatus.Completed,
            planned: ("09:00", "10:00"), actual: ("09:00", "10:00"));
        await SeedTaskAsync(activityId, Today, PlannerTaskStatus.NotStarted, planned: ("09:00", "17:00"));

        var acrossToday = await GetTrendAsync(Today.AddDays(-7), Today);

        acrossToday.TaskCount.Should().Be(1, "only yesterday's task is on a day that is over");
        acrossToday.PlannedMinutes.Should().Be(60, "today's eight-hour plan must not enter the sum");
        acrossToday.NeverHappenedCount.Should().Be(0, "an unstarted task on an unfinished day has not been missed");

        var todayOnly = await GetTrendAsync(Today, Today);

        todayOnly.TaskCount.Should().Be(0, "a range that is entirely still in progress has nothing to say");
    }

    /// <summary>
    /// Background tasks are scaffolding and cancelled tasks are a legitimate way to close a task — both leave
    /// the population entirely, so neither reaches <c>TaskCount</c> and neither can be counted as a task that
    /// never happened. The cancelled half is the same call the streak makes; the two must not diverge.
    /// </summary>
    [Fact]
    public async Task Trend_BackgroundAndCancelledTasks_LeaveThePopulationEntirely()
    {
        var activityId = await SeedActivityAsync("trend-excluded");
        var day = Today.AddDays(-1);

        await SeedTaskAsync(activityId, day, PlannerTaskStatus.Completed,
            planned: ("09:00", "10:00"), actual: ("09:00", "10:00"));
        await SeedTaskAsync(activityId, day, PlannerTaskStatus.NotStarted,
            planned: ("00:00", "08:00"), isBackground: true);
        await SeedTaskAsync(activityId, day, PlannerTaskStatus.Cancelled, planned: ("11:00", "12:00"));

        var trend = await GetTrendAsync(day, day);

        trend.TaskCount.Should().Be(1);
        trend.PlannedMinutes.Should().Be(60);
        trend.NeverHappenedCount.Should().Be(0, "a skip is not a task that never happened");
    }

    /// <summary>
    /// A task the day went past without it counts once, in <c>NeverHappenedCount</c> — keeping its plan, so
    /// the range does not read as perfectly estimated for having been abandoned.
    /// </summary>
    [Fact]
    public async Task Trend_TaskThatNeverHappened_KeepsItsPlanAndIsCountedOnce()
    {
        var activityId = await SeedActivityAsync("trend-missed");
        var day = Today.AddDays(-1);

        await SeedTaskAsync(activityId, day, PlannerTaskStatus.Completed,
            planned: ("09:00", "10:00"), actual: ("09:00", "10:00"));
        await SeedTaskAsync(activityId, day, PlannerTaskStatus.NotStarted, planned: ("11:00", "12:00"));
        await SeedTaskAsync(activityId, day, PlannerTaskStatus.OnHold, planned: ("13:00", "14:00"));

        var trend = await GetTrendAsync(day, day);

        trend.TaskCount.Should().Be(3);
        trend.PlannedMinutes.Should().Be(180);
        trend.ActualMinutes.Should().Be(60);
        trend.NeverHappenedCount.Should().Be(2);
    }

    /// <summary>
    /// A completed task that carries no actual times — every task ticked off a list rather than through the
    /// now-bar — is counted and its plan is summed, but it contributes no logged minutes and is not a task
    /// that never happened. It is unmeasurable, and a zero-drift guess would be the worst of both readings.
    /// </summary>
    [Fact]
    public async Task Trend_CompletedTaskWithoutActualTimes_IsCountedButNotMeasured()
    {
        var activityId = await SeedActivityAsync("trend-unmeasured");
        var day = Today.AddDays(-1);

        await SeedTaskAsync(activityId, day, PlannerTaskStatus.Completed, planned: ("09:00", "10:00"));

        var trend = await GetTrendAsync(day, day);

        trend.TaskCount.Should().Be(1);
        trend.PlannedMinutes.Should().Be(60);
        trend.ActualMinutes.Should().Be(0);
        trend.NeverHappenedCount.Should().Be(0);
        trend.RanLongerCount.Should().Be(0);
    }

    /// <summary>
    /// An empty range answers 200 with six zeroes, not 404 and not an error — the client's contract is
    /// "<c>TaskCount</c> of 0 means render nothing", and a failure status would make that a special case.
    /// </summary>
    [Fact]
    public async Task Trend_RangeEntirelyInTheFuture_IsSixZeroes()
    {
        var trend = await GetTrendAsync(Today.AddDays(1), Today.AddDays(30));

        trend.Should().BeEquivalentTo(new PlanVsActualTrendResponse
        {
            TaskCount = 0,
            PlannedMinutes = 0,
            ActualMinutes = 0,
            StartedLateCount = 0,
            RanLongerCount = 0,
            NeverHappenedCount = 0
        });
    }

    /// <summary>
    /// Another user's identical day must not reach this answer. The Planning slice takes a plain
    /// <c>DbContext</c> and cannot see the host's global <c>IEntityWithUser</c> filter, so this is the guard
    /// that the scoping is actually applied rather than assumed — and it fails silently if it is not: the
    /// numbers merely come back bigger.
    /// </summary>
    [Fact]
    public async Task Trend_AnotherUsersTasks_AreNeverCounted()
    {
        var day = Today.AddDays(-1);
        var otherUserId = await CreateUserAsync("plan-vs-actual-other@example.com");
        var mine = await SeedActivityAsync("trend-mine");
        var theirs = await SeedActivityAsync("trend-theirs", otherUserId);

        await SeedTaskAsync(mine, day, PlannerTaskStatus.Completed,
            planned: ("09:00", "10:00"), actual: ("09:00", "10:00"));
        await SeedTaskAsync(theirs, day, PlannerTaskStatus.Completed,
            planned: ("09:00", "17:00"), actual: ("09:00", "18:00"), userId: otherUserId);

        var trend = await GetTrendAsync(day, day);

        trend.TaskCount.Should().Be(1);
        trend.PlannedMinutes.Should().Be(60);
        trend.ActualMinutes.Should().Be(60);
    }

    /// <summary>
    /// The range is the only thing bounding the scan, so a backwards one is rejected rather than silently
    /// answered as empty — same rule, and the same reason, as the task-summaries window.
    /// </summary>
    [Fact]
    public async Task Trend_WithUntilBeforeFrom_IsRejected()
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/planner-task/plan-vs-actual-trend",
            new { from = Today.AddDays(-1), until = Today.AddDays(-8) },
            JsonOpts,
            CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ─── the PlannerBatchLimits.MaxSummaryRangeDays ceiling ──────────────────

    // The three routes that take a { From, Until } window and are bounded only by it. They are pinned together
    // on purpose: they share one constant and two identically-worded rules across three separate validator
    // classes (PlanVsActualTrendValidator, CalendarTaskSummariesValidator, CalendarFilterValidator), and
    // nothing but a theory over all three makes one of them drifting visible.
    //
    // calendar/filter is here because it used to be the odd one out — PERF-2: same window, no cap at all, so a
    // 1900 → 2100 filter scanned the user's whole calendar table with two correlated subqueries per row.
    // Range_TwoCenturies_IsRejectedOnEveryRangeRoute is the pin on that having been closed.
    public const string TaskSummariesRoute = "api/calendar/task-summaries";
    public const string TrendRoute = "api/planner-task/plan-vs-actual-trend";
    public const string CalendarFilterRoute = "api/calendar/filter";

    /// <summary>
    /// The ceiling is counted <b>inclusively</b> — <c>Until.DayNumber - From.DayNumber + 1</c> — so a year and a
    /// day either side of it is the only pair that tells an off-by-one apart from the rule itself. 2029 is not a
    /// leap year, so 2029-01-01 → 2030-01-01 is exactly 366 days.
    /// </summary>
    [Theory]
    [InlineData(TaskSummariesRoute)]
    [InlineData(TrendRoute)]
    [InlineData(CalendarFilterRoute)]
    public async Task Range_AtTheCeiling_IsAccepted(string route)
    {
        var response = await PostRangeAsync(route, new DateOnly(2029, 1, 1), new DateOnly(2030, 1, 1));

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"{PlannerBatchLimits.MaxSummaryRangeDays} days counted inclusively is the ceiling, not one past it");
    }

    /// <summary>
    /// One day past the ceiling is a 400 naming the limit. This is the assertion that has to fail if the
    /// constant is raised by accident.
    /// </summary>
    [Theory]
    [InlineData(TaskSummariesRoute)]
    [InlineData(TrendRoute)]
    [InlineData(CalendarFilterRoute)]
    public async Task Range_OneDayOverTheCeiling_IsRejectedWithTheLimitInTheMessage(string route)
    {
        var response = await PostRangeAsync(route, new DateOnly(2029, 1, 1), new DateOnly(2030, 1, 2));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(CancellationToken))
            .Should().Contain(PlannerBatchLimits.MaxSummaryRangeDays.ToString(),
                "the caller has to be told what the ceiling is to narrow its window under it");
    }

    /// <summary>
    /// A single day is one day, not zero — the same inclusive count from the other end. The month grid's first
    /// request after a day is picked is exactly this shape.
    /// </summary>
    [Theory]
    [InlineData(TaskSummariesRoute)]
    [InlineData(TrendRoute)]
    [InlineData(CalendarFilterRoute)]
    public async Task Range_OfASingleDay_IsAccepted(string route)
    {
        var response = await PostRangeAsync(route, new DateOnly(2029, 1, 1), new DateOnly(2029, 1, 1));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// A backwards range is rejected on every one of the three, rather than silently answered as empty. Asserted
    /// alongside the ceiling deliberately: the two rules are ordered — the length check is <c>When(Until >= From)</c>,
    /// so an inverted range yields a negative length that would otherwise sail under the ceiling unnoticed.
    /// </summary>
    [Theory]
    [InlineData(TaskSummariesRoute)]
    [InlineData(TrendRoute)]
    [InlineData(CalendarFilterRoute)]
    public async Task Range_WithUntilBeforeFrom_IsRejectedOnEveryRangeRoute(string route)
    {
        var response = await PostRangeAsync(route, new DateOnly(2029, 4, 10), new DateOnly(2029, 4, 1));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// <c>PERF-2</c>, closed: <c>calendar/filter</c> takes the same window as its two siblings and now carries
    /// the same ceiling (<c>CalendarFilterValidator</c>, wired explicitly on <c>FilterCalendarEndpoint</c>
    /// rather than by FastEndpoints' convention binding). A two-century range is the shape the finding named,
    /// and all three routes turn it away.
    /// </summary>
    [Theory]
    [InlineData(TaskSummariesRoute)]
    [InlineData(TrendRoute)]
    [InlineData(CalendarFilterRoute)]
    public async Task Range_TwoCenturies_IsRejectedOnEveryRangeRoute(string route)
    {
        var response = await PostRangeAsync(route, new DateOnly(1900, 1, 1), new DateOnly(2100, 1, 1));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "an unbounded window is the only way any of these three reads can get expensive");
    }

    // ─── helpers ─────────────────────────────────────────────────────────────

    /// <summary>The one wire shape all three range routes share — which is the whole reason they share a ceiling.</summary>
    private async Task<HttpResponseMessage> PostRangeAsync(string route, DateOnly from, DateOnly until) =>
        await CreateClient().PostAsJsonAsync(route, new { From = from, Until = until }, JsonOpts, CancellationToken);

    private async Task<PlanVsActualTrendResponse> GetTrendAsync(DateOnly from, DateOnly until)
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/planner-task/plan-vs-actual-trend",
            new { from, until },
            JsonOpts,
            CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<PlanVsActualTrendResponse>(JsonOpts, CancellationToken);
        body.Should().NotBeNull();
        return body!;
    }

    private async Task<long> SeedActivityAsync(string name, long userId = UserId)
    {
        await using var db = CreateDbContext();
        return await PlanningTestSeedHelper.SeedActivityAsync(db, name, userId, CancellationToken);
    }

    private async Task<long> SeedCalendarAsync(DateOnly date, long userId)
    {
        await using var db = CreateDbContext();
        return await PlanningTestSeedHelper.SeedCalendarAsync(db, date, userId, CancellationToken);
    }

    private async Task<long> SeedTaskAsync(
        long activityId,
        DateOnly date,
        PlannerTaskStatus status,
        (string Start, string End) planned,
        (string Start, string End)? actual = null,
        bool isBackground = false,
        long userId = UserId)
    {
        var calendarId = await SeedCalendarAsync(date, userId);

        await using var db = CreateDbContext();

        var task = new PlannerTask
        {
            UserId = userId,
            ActivityId = activityId,
            CalendarId = calendarId,
            StartTime = Time(planned.Start),
            EndTime = Time(planned.End),
            IsBackground = isBackground,
            Status = status,
            ActualStartTime = actual is { } a ? Time(a.Start) : null,
            ActualEndTime = actual is { } b ? Time(b.End) : null
        };
        db.Set<PlannerTask>().Add(task);
        await db.SaveChangesAsync(CancellationToken);
        return task.Id;
    }

    private static TimeOnly Time(string value) => TimeOnly.ParseExact(value, "HH:mm", CultureInfo.InvariantCulture);

    /// <summary>A second real account — <c>PlannerTask.UserId</c> is a foreign key, so a bare id will not do.</summary>
    private async Task<long> CreateUserAsync(string email)
    {
        using var scope = Fixture.UnauthenticatedFactory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();

        var existing = await userManager.FindByEmailAsync(email);
        if (existing is not null)
            return existing.Id;

        var user = new User
        {
            Email = email,
            UserName = email,
            NormalizedEmail = email.ToUpperInvariant(),
            NormalizedUserName = email.ToUpperInvariant(),
            EmailConfirmed = true,
            Locale = AvailableLocales.En,
            Timezone = TimeZoneInfo.Utc
        };
        var result = await userManager.CreateAsync(user, "Str0ng!Passw0rd");
        result.Succeeded.Should().BeTrue();
        return user.Id;
    }
}
