using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.Planning.application.service.reminder;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Sydowwe.Reminders.domain.entity;
using Sydowwe.Reminders.domain.@enum;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// TEST-3 / Section C — <c>PatchPlannerTaskStatusEndpoint</c> and <c>PatchPlannerTaskSpanEndpoint</c>.
/// </summary>
[Collection("Postgres")]
public class PlannerTaskStatusAndSpanTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    [Fact(DisplayName = "Setting Completed makes IsDone derived-true")]
    public async Task SettingCompleted_MakesIsDoneTrue()
    {
        var (_, taskId) = await SeedTaskAsync(new TimeOnly(9, 0), new TimeOnly(10, 0));

        var response = await PatchStatusAsync(taskId, "Completed");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var db = CreateDbContext();
        var task = await db.Set<PlannerTask>().SingleAsync(t => t.Id == taskId, CancellationToken);
        task.Status.Should().Be(PlannerTaskStatus.Completed);
        task.IsDone.Should().BeTrue("IsDone is derived from Status == Completed");
    }

    [Theory(DisplayName = "Setting Cancelled or NotStarted clears ActualStart/EndTime")]
    [InlineData("Cancelled")]
    [InlineData("NotStarted")]
    public async Task SettingCancelledOrNotStarted_ClearsActualTimes(string status)
    {
        var (_, taskId) = await SeedTaskAsync(new TimeOnly(9, 0), new TimeOnly(10, 0));

        // Get the task into InProgress with actual times set first.
        var started = await PatchStatusAsync(taskId, "InProgress", actualStart: (9, 5));
        started.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using (var db = CreateDbContext())
        {
            var midway = await db.Set<PlannerTask>().SingleAsync(t => t.Id == taskId, CancellationToken);
            midway.ActualStartTime.Should().Be(new TimeOnly(9, 5));
        }

        var response = await PatchStatusAsync(taskId, status);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var finalDb = CreateDbContext();
        var task = await finalDb.Set<PlannerTask>().SingleAsync(t => t.Id == taskId, CancellationToken);
        task.ActualStartTime.Should().BeNull($"{status} clears actual times — this is the reference behaviour CQ-7 says TodoListItemIsDoneChangedEventHandler gets wrong");
        task.ActualEndTime.Should().BeNull();
    }

    [Fact(DisplayName = "A status change retires the task's reminder")]
    public async Task StatusChange_RetiresTheReminder()
    {
        var (_, taskId) = await SeedTaskAsync(new TimeOnly(9, 0), new TimeOnly(10, 0));

        var createdReminder = await CreateClient().PostAsJsonAsync("api/reminder", new
        {
            Title = "Don't forget",
            RemindAt = DateTime.UtcNow.AddHours(2),
            LeadOffsetsMinutes = new[] { 0 },
            PlannerTaskId = taskId
        }, JsonOpts, CancellationToken);
        createdReminder.StatusCode.Should().Be(HttpStatusCode.Created);
        var reminderId = await createdReminder.Content.ReadFromJsonAsync<long>(CancellationToken);

        var patched = await PatchStatusAsync(taskId, "Completed");
        patched.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var db = CreateDbContext();
        var definition = await db.Set<ReminderDefinition>()
            .Where(d => d.OwnerModule == ReminderRegistrationService.OwnerModule
                        && d.SubjectType == ReminderRegistrationService.SubjectType
                        && d.SubjectId == reminderId.ToString()
                        && d.Kind == ReminderRegistrationService.Kind)
            .SingleAsync(CancellationToken);

        definition.Status.Should().Be(ReminderStatus.Cancelled, "finishing the task retires the reminder attached to it");
    }

    [Fact(DisplayName = "Span patch rejects EndTime <= StartTime")]
    public async Task SpanPatch_RejectsEndBeforeOrEqualStart()
    {
        var (_, taskId) = await SeedTaskAsync(new TimeOnly(9, 0), new TimeOnly(10, 0));

        var response = await CreateClient().PatchAsJsonAsync($"api/planner-task/{taskId}", new
        {
            StartTime = new { Hours = 12, Minutes = 0 },
            EndTime = new { Hours = 12, Minutes = 0 }
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "PlannerTaskChangeSpanValidator requires StartTime strictly before EndTime");
    }

    /// <summary>
    /// CQ-14, fixed 2026-09-08. <c>BasePlannerTask.IsNextDay</c> is commented out and <c>TimeOnly</c> cannot
    /// express an overnight span, so a 23:00→01:00 task has nowhere to record "ends the next day". This test
    /// used to be a green probe on the opposite behaviour: <c>PlannerTaskValidator</c> had no ordering rule, so
    /// the create path accepted the shape that <c>PlannerTaskChangeSpanValidator</c> refuses, landing a row with
    /// <c>EndTime &lt; StartTime</c> that <c>TasksOverlap</c> reads as a same-day negative duration and that
    /// <c>PlannerTemplateApplier</c> would delete outright while putting nothing back (<c>CQ-7</c>).
    /// <para>
    /// The two paths now agree. Both directions are asserted here — create and full update — because the same
    /// validator guards both, and it is the update path that a client would otherwise use to walk a task into
    /// the state the patch endpoint refuses.
    /// </para>
    /// </summary>
    [Fact(DisplayName = "CQ-14: creating or updating a 23:00->01:00 task is rejected, matching the span-patch path")]
    public async Task CreatingAnOvernightTask_IsRejected()
    {
        await using var db = CreateDbContext();
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "overnight-cq14", ct: TestContext.Current.CancellationToken);
        var calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, new DateOnly(2027, 4, 1), ct: TestContext.Current.CancellationToken);

        var response = await CreateClient().PostAsJsonAsync("api/planner-task", new
        {
            StartTime = new { Hours = 23, Minutes = 0 },
            EndTime = new { Hours = 1, Minutes = 0 },
            IsBackground = false,
            ActivityId = activityId,
            Status = "NotStarted",
            CalendarId = calendarId
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "PlannerTaskValidator now carries the same StartTime<EndTime rule as PlannerTaskChangeSpanValidator");

        // EndTime == StartTime is the same defect with a zero-length span, and the rule is strict.
        var zeroLength = await CreateClient().PostAsJsonAsync("api/planner-task", new
        {
            StartTime = new { Hours = 9, Minutes = 0 },
            EndTime = new { Hours = 9, Minutes = 0 },
            IsBackground = false,
            ActivityId = activityId,
            Status = "NotStarted",
            CalendarId = calendarId
        }, JsonOpts, CancellationToken);
        zeroLength.StatusCode.Should().Be(HttpStatusCode.BadRequest, "the ordering rule is strictly-before, not before-or-equal");

        // The update path shares PlannerTaskValidator, so a well-formed task cannot be walked into the bad
        // shape either -- that was the hole the span-patch endpoint's own rule could not close.
        var (_, existingTaskId) = await SeedTaskAsync(new TimeOnly(9, 0), new TimeOnly(10, 0));
        var update = await CreateClient().PutAsJsonAsync($"api/planner-task/{existingTaskId}", new
        {
            StartTime = new { Hours = 23, Minutes = 0 },
            EndTime = new { Hours = 1, Minutes = 0 },
            IsBackground = false,
            ActivityId = activityId,
            Status = "NotStarted",
            CalendarId = calendarId
        }, JsonOpts, CancellationToken);
        update.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "UpdatePlannerTaskEndpoint uses the same validator as the create path");

        await using var verifyDb = CreateDbContext();
        var task = await verifyDb.Set<PlannerTask>().SingleAsync(t => t.Id == existingTaskId, CancellationToken);
        task.StartTime.Should().Be(new TimeOnly(9, 0), "the rejected update must not have been applied");
        task.EndTime.Should().Be(new TimeOnly(10, 0));

        var spanFix = await CreateClient().PatchAsJsonAsync($"api/planner-task/{existingTaskId}", new
        {
            StartTime = new { Hours = 23, Minutes = 0 },
            EndTime = new { Hours = 1, Minutes = 0 }
        }, JsonOpts, CancellationToken);
        spanFix.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "the span-patch validator refuses the same shape -- the two paths no longer disagree");
    }

    // ─── helpers ─────────────────────────────────────────────────────────────

    private async Task<(long CalendarId, long TaskId)> SeedTaskAsync(TimeOnly start, TimeOnly end)
    {
        await using var db = CreateDbContext();
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, $"status-span-{Guid.NewGuid():N}");
        var calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, new DateOnly(2027, 4, 2));
        var taskId = await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, calendarId, start, end);
        return (calendarId, taskId);
    }

    private Task<HttpResponseMessage> PatchStatusAsync(long taskId, string status, (int Hours, int Minutes)? actualStart = null)
    {
        object body = actualStart is { } t
            ? new { Status = status, ActualStartTime = new { Hours = t.Hours, Minutes = t.Minutes } }
            : new { Status = status };
        return CreateClient().PatchAsJsonAsync($"api/planner-task/{taskId}/status", body, JsonOpts, CancellationToken);
    }
}
