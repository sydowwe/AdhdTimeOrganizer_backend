using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.IntegrationTests.Reminders;
using AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// The carry-over command (<c>POST /planner-task/{id}/carry-over</c>), its candidates read and the trend's
/// <c>CarriedOverCount</c> — F1-03. A carry-over used to be a client-side copy (the original stayed open on its
/// day, costing the streak and logging a "never happened") and then a plain move (which lost the slip). These pin
/// the three things the command adds: the task leaves its day, it keeps <c>OriginalDate</c>, and the count goes up.
/// <para>The fixture user is on UTC, so the user's today is UTC's.</para>
/// </summary>
[Collection("Postgres")]
public class CarryOverPlannerTaskTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    // ---- the command -----------------------------------------------------

    [Fact(DisplayName = "Carrying over moves the task to the day, records where it came from, and starts it afresh")]
    public async Task CarryOver_MovesTheTask_AndRecordsTheSlip()
    {
        var taskId = await SeedTaskAsync(Today.AddDays(-1), PlannerTaskStatus.InProgress, actualStart: new TimeOnly(9, 5));

        var response = await CarryOverAsync(taskId, Today);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);

        var returned = JsonSerializer.Deserialize<PlannerTaskResponse>(body, JsonOpts)!;
        returned.OriginalDate.Should().Be(Today.AddDays(-1));
        returned.CarryOverCount.Should().Be(1);

        var task = await LoadAsync(taskId);
        task.Calendar.Date.Should().Be(Today, "the task leaves its old day, which is what keeps that day's streak and trend clean");
        task.OriginalDate.Should().Be(Today.AddDays(-1));
        task.CarryOverCount.Should().Be(1);
        task.Status.Should().Be(PlannerTaskStatus.NotStarted, "a carried task is a fresh attempt");
        task.ActualStartTime.Should().BeNull("yesterday's start time on today's block would be a lie");
        task.StartTime.Should().Be(new TimeOnly(9, 0), "its time of day is kept");
    }

    [Fact(DisplayName = "A second carry-over keeps the first original date and counts twice")]
    public async Task CarryOverTwice_KeepsTheFirstOriginalDate()
    {
        var taskId = await SeedTaskAsync(Today.AddDays(-2));

        (await CarryOverAsync(taskId, Today.AddDays(-1))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await CarryOverAsync(taskId, Today)).StatusCode.Should().Be(HttpStatusCode.OK);

        var task = await LoadAsync(taskId);
        task.OriginalDate.Should().Be(Today.AddDays(-2), "the day it was first planned for, set once");
        task.CarryOverCount.Should().Be(2);
    }

    [Fact(DisplayName = "Carrying over to a day with no calendar row creates the day")]
    public async Task CarryOver_ToAnUnseededDay_CreatesIt()
    {
        var taskId = await SeedTaskAsync(Today.AddDays(-1));
        // Outside the years CalendarSeeder fills, so no row can exist for it.
        var farDay = new DateOnly(Today.Year + 3, 3, 15);

        (await CarryOverAsync(taskId, farDay)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await LoadAsync(taskId)).Calendar.Date.Should().Be(farDay);
    }

    [Theory(DisplayName = "A completed or skipped task cannot be carried over")]
    [InlineData(PlannerTaskStatus.Completed)]
    [InlineData(PlannerTaskStatus.Cancelled)]
    public async Task CarryOver_OfAClosedTask_Is400(PlannerTaskStatus status)
    {
        var taskId = await SeedTaskAsync(Today.AddDays(-1), status);

        (await CarryOverAsync(taskId, Today)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await LoadAsync(taskId)).CarryOverCount.Should().Be(0);
    }

    [Fact(DisplayName = "Carrying a task back to its own day or earlier is a 400 — that is a move, not a slip")]
    public async Task CarryOver_ToTheSameOrAnEarlierDay_Is400()
    {
        var taskId = await SeedTaskAsync(Today);

        (await CarryOverAsync(taskId, Today)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CarryOverAsync(taskId, Today.AddDays(-1))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact(DisplayName = "Another user's task reads as missing")]
    public async Task CarryOver_OfAnotherUsersTask_Is404()
    {
        long taskId;
        await using (var db = CreateDbContext())
        {
            await ReminderSeedHelper.EnsureOtherUserAsync(db, CancellationToken);
            taskId = await SeedTaskAsync(Today.AddDays(-1), userId: ReminderSeedHelper.OtherUserId);
        }

        (await CarryOverAsync(taskId, Today)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- the candidates read ---------------------------------------------

    [Fact(DisplayName = "Candidates are the open, non-background tasks of the last week, newest day first")]
    public async Task Candidates_AreTheLastWeeksOpenTasks()
    {
        var yesterdayOpen = await SeedTaskAsync(Today.AddDays(-1), name: "yesterday-open");
        var threeDaysAgo = await SeedTaskAsync(Today.AddDays(-3), PlannerTaskStatus.InProgress, name: "three-days-ago");
        await SeedTaskAsync(Today.AddDays(-8), name: "past-the-week");
        await SeedTaskAsync(Today.AddDays(-1), PlannerTaskStatus.Completed, name: "done");
        await SeedTaskAsync(Today.AddDays(-1), PlannerTaskStatus.Cancelled, name: "skipped");
        await SeedTaskAsync(Today.AddDays(-1), isBackground: true, name: "background");
        await SeedTaskAsync(Today, name: "today");

        var response = await CreateClient().GetAsync("api/planner-task/carry-over-candidates", CancellationToken);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        var candidates = JsonSerializer.Deserialize<List<CarryOverCandidateResponse>>(body, JsonOpts)!;

        candidates.Select(c => c.Task.Id).Should().Equal([yesterdayOpen, threeDaysAgo],
            "newest day first; a skip is a decision, not something left behind, and the week is the horizon");
        candidates[1].Date.Should().Be(Today.AddDays(-3));
    }

    // ---- the trend -------------------------------------------------------

    [Fact(DisplayName = "The trend reports carry-overs on their own, not as tasks that never happened")]
    public async Task Trend_CountsCarryOvers_Separately()
    {
        var carried = await SeedTaskAsync(Today.AddDays(-2), name: "carried");
        await SeedTaskAsync(Today.AddDays(-2), name: "left-open");
        (await CarryOverAsync(carried, Today.AddDays(-1))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await CarryOverAsync(carried, Today)).StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await CreateClient().PostAsJsonAsync("api/planner-task/plan-vs-actual-trend",
            new { From = Today.AddDays(-2).ToString("yyyy-MM-dd"), Until = Today.AddDays(-1).ToString("yyyy-MM-dd") },
            JsonOpts, CancellationToken);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        var trend = JsonSerializer.Deserialize<PlanVsActualTrendResponse>(body, JsonOpts)!;

        trend.CarriedOverCount.Should().Be(2, "one task first planned in the range, pushed twice");
        trend.NeverHappenedCount.Should().Be(1, "only the task left open on its day — the carried one left");
        trend.TaskCount.Should().Be(1);
    }

    // ---- helpers ---------------------------------------------------------

    private async Task<HttpResponseMessage> CarryOverAsync(long taskId, DateOnly date) =>
        await CreateClient().PostAsJsonAsync($"api/planner-task/{taskId}/carry-over",
            new { Date = date.ToString("yyyy-MM-dd") }, JsonOpts, CancellationToken);

    private async Task<long> SeedTaskAsync(
        DateOnly date,
        PlannerTaskStatus status = PlannerTaskStatus.NotStarted,
        TimeOnly? actualStart = null,
        bool isBackground = false,
        string? name = null,
        long userId = UserId)
    {
        await using var db = CreateDbContext();
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, name ?? $"carry-{Guid.NewGuid():N}", userId, CancellationToken);
        var calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, date, userId, CancellationToken);
        var taskId = await PlanningTestSeedHelper.SeedPlannerTaskAsync(
            db, activityId, calendarId, new TimeOnly(9, 0), new TimeOnly(10, 0), userId, status, ct: CancellationToken);

        if (actualStart != null || isBackground)
        {
            var task = await db.Set<PlannerTask>().IgnoreQueryFilters().SingleAsync(t => t.Id == taskId, CancellationToken);
            task.ActualStartTime = actualStart;
            task.IsBackground = isBackground;
            await db.SaveChangesAsync(CancellationToken);
        }

        return taskId;
    }

    private async Task<PlannerTask> LoadAsync(long taskId)
    {
        await using var db = CreateDbContext();
        return await db.Set<PlannerTask>().IgnoreQueryFilters().AsNoTracking()
            .Include(t => t.Calendar)
            .SingleAsync(t => t.Id == taskId, CancellationToken);
    }
}
