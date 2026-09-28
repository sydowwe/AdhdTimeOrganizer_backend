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

    // ─── B26: skipReason — written by the status patch only, and only on a Cancelled task ───────────────

    [Fact(DisplayName = "B26: skipping stores the trimmed reason, a re-skip overwrites it, and restoring clears it")]
    public async Task SkipReason_StoredOnCancel_OverwrittenOnReskip_ClearedOnRestore()
    {
        var (_, taskId) = await SeedTaskAsync(new TimeOnly(9, 0), new TimeOnly(10, 0));

        (await PatchStatusWithReasonAsync(taskId, "cancelled", "  Meeting ran long  ")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await LoadAsync(taskId)).SkipReason.Should().Be("Meeting ran long",
            "the status patch used to drop this key silently — the value must land, trimmed");

        (await PatchStatusWithReasonAsync(taskId, "cancelled", "Felt unwell")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await LoadAsync(taskId)).SkipReason.Should().Be("Felt unwell", "a re-skip overwrites the old reason");

        // The home widget's Restore: notStarted with an explicit null reason.
        (await PatchStatusWithReasonAsync(taskId, "notStarted", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var restored = await LoadAsync(taskId);
        restored.Status.Should().Be(PlannerTaskStatus.NotStarted);
        restored.SkipReason.Should().BeNull("a restored task must not keep the reason for a skip that no longer holds");
    }

    [Fact(DisplayName = "B26: completing a skipped task clears its reason")]
    public async Task SkipReason_ClearedWhenCancelledTaskIsCompleted()
    {
        var (_, taskId) = await SeedTaskAsync(new TimeOnly(9, 0), new TimeOnly(10, 0));
        (await PatchStatusWithReasonAsync(taskId, "cancelled", "Injured")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await PatchStatusWithReasonAsync(taskId, "completed", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await LoadAsync(taskId)).SkipReason.Should().BeNull("an orphaned skip reason on a completed row is exactly the bug rule 2 closes");
    }

    [Fact(DisplayName = "B26: a blank reason is stored as null")]
    public async Task SkipReason_BlankIsNull()
    {
        var (_, taskId) = await SeedTaskAsync(new TimeOnly(9, 0), new TimeOnly(10, 0));

        (await PatchStatusWithReasonAsync(taskId, "cancelled", "   ")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var task = await LoadAsync(taskId);
        task.Status.Should().Be(PlannerTaskStatus.Cancelled);
        task.SkipReason.Should().BeNull();
    }

    [Theory(DisplayName = "B26: a reason beside any status but Cancelled is a 400; a null one is accepted")]
    [InlineData("notStarted")]
    [InlineData("inProgress")]
    [InlineData("completed")]
    public async Task SkipReason_WithNonCancelledStatus_IsRejected(string status)
    {
        var (_, taskId) = await SeedTaskAsync(new TimeOnly(9, 0), new TimeOnly(10, 0));

        var rejected = await PatchStatusWithReasonAsync(taskId, status, "Ran out of time");
        rejected.StatusCode.Should().Be(HttpStatusCode.BadRequest, "a value the status would silently drop is refused, not swallowed");
        (await LoadAsync(taskId)).Status.Should().Be(PlannerTaskStatus.NotStarted, "the rejected patch must not have been applied");

        (await PatchStatusWithReasonAsync(taskId, status, null)).StatusCode.Should().Be(HttpStatusCode.NoContent,
            "the client always sends skipReason: null beside a non-cancelled status");
    }

    [Fact(DisplayName = "B26: a reason over 500 characters is a 400")]
    public async Task SkipReason_OverColumnLength_IsRejected()
    {
        var (_, taskId) = await SeedTaskAsync(new TimeOnly(9, 0), new TimeOnly(10, 0));

        (await PatchStatusWithReasonAsync(taskId, "cancelled", new string('x', 501))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await PatchStatusWithReasonAsync(taskId, "cancelled", new string('x', 500))).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact(DisplayName = "B26: the full PUT keeps the reason while the task stays Cancelled, never writes it, and clears it on leaving")]
    public async Task SkipReason_FullPut_KeepsOnCancelled_ClearsOnLeaving()
    {
        var (calendarId, taskId) = await SeedTaskAsync(new TimeOnly(9, 0), new TimeOnly(10, 0));
        (await PatchStatusWithReasonAsync(taskId, "cancelled", "Meeting ran long")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var activityId = (await LoadAsync(taskId)).ActivityId;

        // A skipped task moved later, with whatever reason the client last read in the body.
        (await PutAsync(taskId, activityId, calendarId, "Cancelled", 11, "Something else")).StatusCode.Should().Be(HttpStatusCode.OK);
        var moved = await LoadAsync(taskId);
        moved.StartTime.Should().Be(new TimeOnly(11, 0));
        moved.SkipReason.Should().Be("Meeting ran long", "the PUT is not a writer of the reason; the status patch is");

        (await PutAsync(taskId, activityId, calendarId, "NotStarted", 12, "Ran out of time")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await LoadAsync(taskId)).SkipReason.Should().BeNull("a PUT that leaves Cancelled clears the reason, same as the status patch");
    }

    // ─── B1: skipReasonKey — the three built-in reasons stored as keys ────────────────────────────────

    [Fact(DisplayName = "B1: a key is stored as the key, with no text, and reads back camelCase on the response")]
    public async Task SkipReasonKey_StoredAndReadBack()
    {
        var (_, taskId) = await SeedTaskAsync(new TimeOnly(9, 0), new TimeOnly(10, 0));

        (await PatchStatusWithKeyAsync(taskId, "cancelled", "notRelevant", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var task = await LoadAsync(taskId);
        task.SkipReasonKey.Should().Be(SkipReasonKey.NotRelevant);
        task.SkipReason.Should().BeNull();

        var response = await CreateClient().GetFromJsonAsync<PlannerTaskResponse>($"api/planner-task/{taskId}", JsonOpts, CancellationToken);
        response!.SkipReasonKey.Should().Be("notRelevant", "the wire spelling is the client's, not the C# member name");
        response.SkipReason.Should().BeNull();
    }

    /// <summary>
    /// The contract is that the six built-in labels never come back as text. The migration converted the rows
    /// already written; this is the write-side half, which holds whichever client — old home widget, day planner
    /// — sends the label as text.
    /// </summary>
    [Theory(DisplayName = "B1: a built-in label sent as text, in either locale, is stored as its key")]
    [InlineData("Ran out of time", SkipReasonKey.NoTime)]
    [InlineData("Nestihol som", SkipReasonKey.NoTime)]
    [InlineData("Not relevant any more", SkipReasonKey.NotRelevant)]
    [InlineData("Už nie je aktuálne", SkipReasonKey.NotRelevant)]
    [InlineData("No energy for it", SkipReasonKey.NoEnergy)]
    [InlineData("  Nemal som energiu ", SkipReasonKey.NoEnergy)]
    public async Task SkipReason_BuiltInLabel_IsStoredAsKey(string label, SkipReasonKey expected)
    {
        var (_, taskId) = await SeedTaskAsync(new TimeOnly(9, 0), new TimeOnly(10, 0));

        (await PatchStatusWithReasonAsync(taskId, "cancelled", label)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var task = await LoadAsync(taskId);
        task.SkipReasonKey.Should().Be(expected);
        task.SkipReason.Should().BeNull("at most one of the two is ever set");
    }

    [Theory(DisplayName = "B1: a near-miss of a built-in label stays text")]
    [InlineData("ran out of time")]
    [InlineData("Ran out of time again")]
    public async Task SkipReason_NearMissOfALabel_StaysText(string text)
    {
        var (_, taskId) = await SeedTaskAsync(new TimeOnly(9, 0), new TimeOnly(10, 0));

        (await PatchStatusWithReasonAsync(taskId, "cancelled", text)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var task = await LoadAsync(taskId);
        task.SkipReason.Should().Be(text);
        task.SkipReasonKey.Should().BeNull();
    }

    [Theory(DisplayName = "B1: an unknown key, a key beside text, and a key beside any status but Cancelled are all 400s")]
    [InlineData("cancelled", "tooTired", null)]
    [InlineData("cancelled", "NoTime", null)]
    [InlineData("cancelled", "noTime", "Meeting ran long")]
    [InlineData("completed", "noTime", null)]
    [InlineData("notStarted", "noTime", null)]
    public async Task SkipReasonKey_InvalidCombinations_AreRejected(string status, string key, string? text)
    {
        var (_, taskId) = await SeedTaskAsync(new TimeOnly(9, 0), new TimeOnly(10, 0));

        (await PatchStatusWithKeyAsync(taskId, status, key, text)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var task = await LoadAsync(taskId);
        task.Status.Should().Be(PlannerTaskStatus.NotStarted, "the rejected patch must not have been applied");
        task.SkipReasonKey.Should().BeNull();
    }

    [Fact(DisplayName = "B1: leaving Cancelled clears the key, through the status patch and through the PUT")]
    public async Task SkipReasonKey_ClearedOnLeavingCancelled()
    {
        var (calendarId, taskId) = await SeedTaskAsync(new TimeOnly(9, 0), new TimeOnly(10, 0));
        var activityId = (await LoadAsync(taskId)).ActivityId;

        (await PatchStatusWithKeyAsync(taskId, "cancelled", "noTime", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await PatchStatusWithReasonAsync(taskId, "notStarted", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await LoadAsync(taskId)).SkipReasonKey.Should().BeNull("a restored task must not keep the key for a skip that no longer holds");

        (await PatchStatusWithKeyAsync(taskId, "cancelled", "noEnergy", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await PutAsync(taskId, activityId, calendarId, "Cancelled", 11, null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await LoadAsync(taskId)).SkipReasonKey.Should().Be(SkipReasonKey.NoEnergy, "a skipped task moved later keeps its key");

        (await PutAsync(taskId, activityId, calendarId, "NotStarted", 12, null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await LoadAsync(taskId)).SkipReasonKey.Should().BeNull("a PUT that leaves Cancelled clears the key, same as the status patch");
    }

    // ─── helpers ─────────────────────────────────────────────────────────────

    private Task<HttpResponseMessage> PatchStatusWithKeyAsync(long taskId, string status, string? skipReasonKey, string? skipReason) =>
        CreateClient().PatchAsJsonAsync($"api/planner-task/{taskId}/status",
            new { status, actualStartTime = (object?)null, actualEndTime = (object?)null, skipReason, skipReasonKey }, JsonOpts, CancellationToken);

    private async Task<PlannerTask> LoadAsync(long taskId)
    {
        await using var db = CreateDbContext();
        return await db.Set<PlannerTask>().AsNoTracking().SingleAsync(t => t.Id == taskId, CancellationToken);
    }

    // Mirrors the client's exact body: all four keys, camelCase status, explicit nulls.
    private Task<HttpResponseMessage> PatchStatusWithReasonAsync(long taskId, string status, string? skipReason) =>
        CreateClient().PatchAsJsonAsync($"api/planner-task/{taskId}/status",
            new { status, actualStartTime = (object?)null, actualEndTime = (object?)null, skipReason }, JsonOpts, CancellationToken);

    private Task<HttpResponseMessage> PutAsync(long taskId, long activityId, long calendarId, string status, int startHour, string? skipReason) =>
        CreateClient().PutAsJsonAsync($"api/planner-task/{taskId}", new
        {
            StartTime = new { Hours = startHour, Minutes = 0 },
            EndTime = new { Hours = startHour + 1, Minutes = 0 },
            IsBackground = false,
            ActivityId = activityId,
            Status = status,
            CalendarId = calendarId,
            SkipReason = skipReason
        }, JsonOpts, CancellationToken);

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
