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
    /// CQ-14 probe. <c>BasePlannerTask.IsNextDay</c> is commented out and <c>TimeOnly</c> cannot express an
    /// overnight span, so a 23:00→01:00 task has nowhere to record "ends the next day". Unlike the span-patch
    /// path (<c>PlannerTaskChangeSpanValidator</c> rejects <c>EndTime &lt;= StartTime</c>), the create/update
    /// path's <c>PlannerTaskValidator</c> has <b>no such rule</b> — only per-field hour/minute range checks —
    /// so this is expected to be <b>accepted</b>, landing a row with <c>EndTime &lt; StartTime</c> that every
    /// overlap computation (<c>TasksOverlap</c>) then reads as a same-day, negative-duration span. That is the
    /// finding: create and patch-span disagree about whether an overnight task is even expressible.
    /// </summary>
    [Fact(DisplayName = "CQ-14: creating a 23:00->01:00 task is accepted, producing a negative same-day duration")]
    public async Task CreatingAnOvernightTask_IsAcceptedWithNegativeDuration()
    {
        await using var db = CreateDbContext();
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "overnight-cq14");
        var calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, new DateOnly(2027, 4, 1));

        var response = await CreateClient().PostAsJsonAsync("api/planner-task", new
        {
            StartTime = new { Hours = 23, Minutes = 0 },
            EndTime = new { Hours = 1, Minutes = 0 },
            IsBackground = false,
            ActivityId = activityId,
            Status = "NotStarted",
            CalendarId = calendarId
        }, JsonOpts, CancellationToken);

        // Documenting current behaviour, not endorsing it: PlannerTaskValidator has no StartTime<EndTime rule,
        // so this is accepted. If a future fix adds the rule here (to match PlannerTaskChangeSpanValidator),
        // this assertion is the one to flip to BadRequest.
        response.StatusCode.Should().Be(HttpStatusCode.Created,
            "PlannerTaskValidator, unlike PlannerTaskChangeSpanValidator, has no ordering rule on StartTime/EndTime");

        var taskId = await response.Content.ReadFromJsonAsync<long>(CancellationToken);
        await using var verifyDb = CreateDbContext();
        var task = await verifyDb.Set<PlannerTask>().SingleAsync(t => t.Id == taskId, CancellationToken);
        (task.EndTime < task.StartTime).Should().BeTrue(
            "TimeOnly has no day component, so 01:00 stored as 'EndTime' is earlier than 23:00 'StartTime' -- a negative same-day duration");

        // Once created, the task cannot be normalised back into range through the span-patch endpoint: that
        // validator rejects EndTime <= StartTime outright, so the only way out of the bad state this endpoint
        // created is a full PUT through UpdatePlannerTaskEndpoint (same lenient validator) or direct deletion.
        var spanFix = await CreateClient().PatchAsJsonAsync($"api/planner-task/{taskId}", new
        {
            StartTime = new { Hours = 23, Minutes = 0 },
            EndTime = new { Hours = 1, Minutes = 0 }
        }, JsonOpts, CancellationToken);
        spanFix.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "the span-patch validator refuses the very shape the create endpoint just accepted");
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
