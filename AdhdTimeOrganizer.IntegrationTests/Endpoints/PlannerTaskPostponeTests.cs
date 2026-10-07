using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// FO3 phase 2 through the real write paths: the PUT (home's <i>Move later</i>, the day view's reschedule) and the
/// span patch (a drag) both feed <c>PlannerTaskPostponeRule</c>, the carry-over command fixes the same original
/// slot without counting a postponement, and the trend measures a snoozed block's lateness from that slot.
/// <para>
/// This mechanism fails <b>silently</b>: a write path that skips the recorder answers 200 and simply leaves no
/// trace. So every case asserts on the stored row. The rule's own edges are pinned without a database in
/// <c>Services.PlannerTaskPostponeRuleTests</c>.
/// </para>
/// <para>The fixture user is on UTC, so the user's today is UTC's. Every slot a case expects to have passed is
/// on yesterday, so no case depends on the hour the suite runs at.</para>
/// </summary>
[Collection("Postgres")]
public class PlannerTaskPostponeTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
    private static DateOnly Yesterday => Today.AddDays(-1);

    [Fact(DisplayName = "FO3: moving a missed block to today with the PUT is a postponement")]
    public async Task Put_MissedBlockToToday_Counts()
    {
        var (taskId, activityId) = await SeedTaskAsync(Yesterday);

        await PutAsync(taskId, activityId, hour: 9, date: Today);

        var task = await LoadAsync(taskId);
        task.Calendar.Date.Should().Be(Today);
        task.PostponeCount.Should().Be(1);
        task.OriginalDate.Should().Be(Yesterday);
        task.OriginalStartTime.Should().Be(new TimeOnly(9, 0));
        task.CarryOverCount.Should().Be(0, "a carry-over is the carry command's count, not this one");
    }

    [Fact(DisplayName = "FO3: dragging a passed block later counts every time, and keeps the first original slot")]
    public async Task Patch_PassedBlockLater_CountsEachTime()
    {
        var (taskId, _) = await SeedTaskAsync(Yesterday);

        await PatchSpanAsync(taskId, 11);
        await PatchSpanAsync(taskId, 13);

        var task = await LoadAsync(taskId);
        task.StartTime.Should().Be(new TimeOnly(13, 0));
        task.PostponeCount.Should().Be(2);
        task.OriginalDate.Should().Be(Yesterday);
        task.OriginalStartTime.Should().Be(new TimeOnly(9, 0));
    }

    [Fact(DisplayName = "FO3: rearranging a future day, or moving a block earlier, leaves no trace")]
    public async Task Replans_DoNotCount()
    {
        var (future, futureActivity) = await SeedTaskAsync(Today.AddDays(2));
        await PutAsync(future, futureActivity, hour: 11, date: Today.AddDays(2));
        await PatchSpanAsync(future, 14);

        var (earlier, _) = await SeedTaskAsync(Yesterday);
        await PatchSpanAsync(earlier, 7);

        foreach (var id in new[] { future, earlier })
        {
            var task = await LoadAsync(id);
            task.PostponeCount.Should().Be(0);
            task.OriginalDate.Should().BeNull();
            task.OriginalStartTime.Should().BeNull();
        }
    }

    [Fact(DisplayName = "FO3: moving a started block is re-arranging work, not putting it off")]
    public async Task StartedBlock_DoesNotCount()
    {
        var (taskId, _) = await SeedTaskAsync(Yesterday, PlannerTaskStatus.InProgress);

        await PatchSpanAsync(taskId, 11);

        (await LoadAsync(taskId)).PostponeCount.Should().Be(0);
    }

    [Fact(DisplayName = "FO3: a carry-over fixes the original slot as a pair and does not count a postponement")]
    public async Task CarryOver_SetsTheOriginalPair_WithoutAPostponement()
    {
        var (taskId, _) = await SeedTaskAsync(Yesterday);

        var carry = await CreateClient().PostAsJsonAsync($"api/planner-task/{taskId}/carry-over",
            new { Date = Today.ToString("yyyy-MM-dd") }, JsonOpts, CancellationToken);
        carry.StatusCode.Should().Be(HttpStatusCode.OK, await carry.Content.ReadAsStringAsync(CancellationToken));

        var task = await LoadAsync(taskId);
        task.OriginalDate.Should().Be(Yesterday);
        task.OriginalStartTime.Should().Be(new TimeOnly(9, 0));
        task.CarryOverCount.Should().Be(1);
        task.PostponeCount.Should().Be(0, "the two counts are disjoint, so one slip is never counted twice");
    }

    /// <summary>
    /// Before this, a block snoozed from 09:00 to 11:00 and started at 11:00 read as on time: the trend compared
    /// the actual start with the snoozed one. The control block was started on time and stays on time.
    /// </summary>
    [Fact(DisplayName = "FO3: the trend counts a snoozed block started at its new time as started late")]
    public async Task Trend_MeasuresASnoozedBlockFromItsOriginalStart()
    {
        var (snoozed, _) = await SeedTaskAsync(Yesterday);
        await PatchSpanAsync(snoozed, 11);
        await CompleteAsync(snoozed, new TimeOnly(11, 0), new TimeOnly(12, 0));

        var (onTime, _) = await SeedTaskAsync(Yesterday);
        await CompleteAsync(onTime, new TimeOnly(9, 0), new TimeOnly(10, 0));

        var response = await CreateClient().PostAsJsonAsync("api/planner-task/plan-vs-actual-trend",
            new { From = Yesterday.ToString("yyyy-MM-dd"), Until = Yesterday.ToString("yyyy-MM-dd") },
            JsonOpts, CancellationToken);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        var trend = JsonSerializer.Deserialize<PlanVsActualTrendResponse>(body, JsonOpts)!;

        trend.TaskCount.Should().Be(2);
        trend.StartedLateCount.Should().Be(1, "two hours after the start it was first planned for");
        trend.RanLongerCount.Should().Be(0, "the span is still the planned one");
    }

    // ---------------------------------------------------------------- helpers

    private async Task<(long TaskId, long ActivityId)> SeedTaskAsync(
        DateOnly date, PlannerTaskStatus status = PlannerTaskStatus.NotStarted)
    {
        await using var db = CreateDbContext();
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, $"postpone-{Guid.NewGuid():N}", UserId, CancellationToken);
        var calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, date, UserId, CancellationToken);
        var taskId = await PlanningTestSeedHelper.SeedPlannerTaskAsync(
            db, activityId, calendarId, new TimeOnly(9, 0), new TimeOnly(10, 0), UserId, status, ct: CancellationToken);
        return (taskId, activityId);
    }

    private async Task PutAsync(long taskId, long activityId, int hour, DateOnly date)
    {
        var response = await CreateClient().PutAsJsonAsync($"api/planner-task/{taskId}", new
        {
            StartTime = new { Hours = hour, Minutes = 0 },
            EndTime = new { Hours = hour + 1, Minutes = 0 },
            IsBackground = false,
            ActivityId = activityId,
            Status = "NotStarted",
            Date = date
        }, JsonOpts, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(CancellationToken));
    }

    private async Task PatchSpanAsync(long taskId, int hour)
    {
        var response = await CreateClient().PatchAsJsonAsync($"api/planner-task/{taskId}", new
        {
            StartTime = new { Hours = hour, Minutes = 0 },
            EndTime = new { Hours = hour + 1, Minutes = 0 }
        }, JsonOpts, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync(CancellationToken));
    }

    /// <summary>Straight onto the row: completing is the status endpoint's rule, and here the row is the input.</summary>
    private async Task CompleteAsync(long taskId, TimeOnly actualStart, TimeOnly actualEnd)
    {
        await using var db = CreateDbContext();
        var task = await db.Set<PlannerTask>().IgnoreQueryFilters().SingleAsync(t => t.Id == taskId, CancellationToken);
        task.Status = PlannerTaskStatus.Completed;
        task.ActualStartTime = actualStart;
        task.ActualEndTime = actualEnd;
        await db.SaveChangesAsync(CancellationToken);
    }

    private async Task<PlannerTask> LoadAsync(long taskId)
    {
        await using var db = CreateDbContext();
        return await db.Set<PlannerTask>().IgnoreQueryFilters().AsNoTracking()
            .Include(t => t.Calendar)
            .SingleAsync(t => t.Id == taskId, CancellationToken);
    }
}
