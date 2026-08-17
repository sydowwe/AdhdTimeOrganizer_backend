using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.IntegrationTests.Reminders;
using AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.Planning.application.service.reminder;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.model.entity.reminder;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Sydowwe.Reminders.domain.entity;
using Sydowwe.Reminders.domain.@enum;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// TEST-3 / Section B — <c>ApplyTemplatePlannerTaskEndpoint</c>'s four conflict-resolution modes. Each test
/// uses a deliberately awkward geometry per the testing prompt: an existing task fully containing a new one
/// (or vice versa), partial overlap at each end, and one blocker in the middle of a long task so carving must
/// yield two segments from one.
/// </summary>
[Collection("Postgres")]
public class ApplyTemplatePlannerTaskConflictResolutionTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    /// <summary>
    /// Ignore: any new task that overlaps an existing one at all is dropped whole; non-conflicting new tasks
    /// are still added, and every existing task is left untouched.
    /// </summary>
    [Fact]
    public async Task Ignore_DropsConflictingNewTasksWhole_KeepsExistingUntouched()
    {
        var (activityId, template) = await SeedApplyFixtureAsync("ignore");
        var calendarId = await SeedCalendarAsync(new DateOnly(2027, 3, 1));

        await using (var db = CreateDbContext())
        {
            await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, calendarId, new TimeOnly(10, 0), new TimeOnly(11, 0)); // E1
            await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, calendarId, new TimeOnly(14, 0), new TimeOnly(16, 0)); // E2
        }

        var response = await ApplyAsync(calendarId, template.Id, "Ignore",
        [
            NewTask(activityId, 10, 30, 10, 45), // fully inside E1 -> dropped
            NewTask(activityId, 13, 0, 15, 0),   // partial overlap with E2 -> dropped
            NewTask(activityId, 17, 0, 18, 0)    // no overlap -> kept
        ]);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<ApplyTemplatePlannerTaskResponse>(JsonOpts, CancellationToken);

        body!.Tasks.Select(t => (t.StartTime.Hours, t.StartTime.Minutes, t.EndTime.Hours, t.EndTime.Minutes))
            .Should().BeEquivalentTo(
            [
                (10, 0, 11, 0),   // E1 untouched
                (14, 0, 16, 0),   // E2 untouched
                (17, 0, 18, 0)    // only the non-conflicting new task
            ]);

        await AssertTemplateStampAsync(template.Id, calendarId, expectedUsageCount: 1);
    }

    /// <summary>
    /// Overwrite: any existing task overlapping any new task is deleted whole, and every new task is added
    /// regardless of overlap. Existing tasks with a reminder attached must have that reminder cancelled.
    /// </summary>
    [Fact]
    public async Task Overwrite_DeletesConflictingExistingTasksWhole_AddsAllNewTasks()
    {
        var (activityId, template) = await SeedApplyFixtureAsync("overwrite");
        var calendarId = await SeedCalendarAsync(new DateOnly(2027, 3, 2));

        long survivorId;
        long removedWithReminderId;
        await using (var db = CreateDbContext())
        {
            removedWithReminderId = await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, calendarId, new TimeOnly(9, 0), new TimeOnly(10, 0)); // fully overlapped
            await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, calendarId, new TimeOnly(15, 0), new TimeOnly(16, 0)); // partially overlapped
            survivorId = await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, calendarId, new TimeOnly(20, 0), new TimeOnly(21, 0)); // untouched
        }

        // A reminder on a task Overwrite is about to delete — its module-side registration must be cancelled.
        var reminderId = await CreateTaskReminderAsync(removedWithReminderId);

        var response = await ApplyAsync(calendarId, template.Id, "Overwrite",
        [
            NewTask(activityId, 8, 30, 10, 30), // fully overlaps the first existing task
            NewTask(activityId, 15, 30, 16, 30) // partially overlaps the second existing task
        ]);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<ApplyTemplatePlannerTaskResponse>(JsonOpts, CancellationToken);

        body!.Tasks.Select(t => (t.StartTime.Hours, t.StartTime.Minutes, t.EndTime.Hours, t.EndTime.Minutes))
            .Should().BeEquivalentTo(
            [
                (8, 30, 10, 30),
                (15, 30, 16, 30),
                (20, 0, 21, 0) // survivor
            ]);

        await using (var db = CreateDbContext())
        {
            (await db.Set<PlannerTask>().IgnoreQueryFilters().AnyAsync(t => t.Id == removedWithReminderId, CancellationToken))
                .Should().BeFalse("the conflicting existing task was deleted");
            (await db.Set<PlannerTask>().IgnoreQueryFilters().AnyAsync(t => t.Id == survivorId, CancellationToken))
                .Should().BeTrue();
        }

        await AssertReminderCancelledAsync(reminderId);
        await AssertTemplateStampAsync(template.Id, calendarId, expectedUsageCount: 1);
    }

    /// <summary>
    /// MergeIgnore: new tasks are carved around existing blockers. A new task fully inside an existing one is
    /// dropped entirely; one in the middle of an existing blocker splits into two segments; overlap at either
    /// end shortens the new task instead of dropping it. Existing tasks are never touched.
    /// </summary>
    [Fact]
    public async Task MergeIgnore_CarvesNewTasksAroundExisting()
    {
        var (activityId, template) = await SeedApplyFixtureAsync("mergeignore");
        var calendarId = await SeedCalendarAsync(new DateOnly(2027, 3, 3));

        await using (var db = CreateDbContext())
        {
            await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, calendarId, new TimeOnly(10, 0), new TimeOnly(11, 0)); // E1: blocker in the middle
            await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, calendarId, new TimeOnly(14, 0), new TimeOnly(15, 0)); // E2: fully contains a new task
            await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, calendarId, new TimeOnly(18, 0), new TimeOnly(19, 0)); // E3: overlapped at both ends by two different new tasks
        }

        var response = await ApplyAsync(calendarId, template.Id, "MergeIgnore",
        [
            NewTask(activityId, 9, 0, 12, 0),     // N1: spans across E1 -> two segments [9-10] and [11-12]
            NewTask(activityId, 14, 15, 14, 45),  // N2: fully inside E2 -> dropped
            NewTask(activityId, 17, 30, 18, 30),  // N3: overlaps the start of E3 -> shortened to [17:30-18:00]
            NewTask(activityId, 18, 30, 19, 30),  // N4: overlaps the end of E3 -> shortened to [19:00-19:30]
            NewTask(activityId, 20, 0, 21, 0)     // N5: no overlap -> kept whole
        ]);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<ApplyTemplatePlannerTaskResponse>(JsonOpts, CancellationToken);

        body!.Tasks.Select(t => (t.StartTime.Hours, t.StartTime.Minutes, t.EndTime.Hours, t.EndTime.Minutes))
            .Should().BeEquivalentTo(
            [
                (10, 0, 11, 0),  // E1 untouched
                (14, 0, 15, 0),  // E2 untouched
                (18, 0, 19, 0),  // E3 untouched
                (9, 0, 10, 0),   // N1 segment 1
                (11, 0, 12, 0),  // N1 segment 2
                (17, 30, 18, 0), // N3 shortened
                (19, 0, 19, 30), // N4 shortened
                (20, 0, 21, 0)   // N5 whole
                // N2 dropped entirely -- fully inside E2
            ], "existing tasks survive untouched and new tasks are carved (or dropped) around them");
    }

    /// <summary>
    /// MergeOverwrite: existing tasks are carved around new blockers, mirroring MergeIgnore. An existing task
    /// fully inside a new one is deleted entirely; one straddled by a new blocker in its middle splits into two
    /// segments; every new task is added as-is. Deleted originals with reminders must have them cancelled.
    /// </summary>
    [Fact]
    public async Task MergeOverwrite_CarvesExistingTasksAroundNew()
    {
        var (activityId, template) = await SeedApplyFixtureAsync("mergeoverwrite");
        var calendarId = await SeedCalendarAsync(new DateOnly(2027, 3, 4));

        long middleBlockedId, fullyContainedId, untouchedId;
        await using (var db = CreateDbContext())
        {
            middleBlockedId = await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, calendarId, new TimeOnly(9, 0), new TimeOnly(12, 0));  // E1: long, blocker lands in the middle
            fullyContainedId = await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, calendarId, new TimeOnly(14, 15), new TimeOnly(14, 45)); // E2: fully inside a new task
            untouchedId = await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, calendarId, new TimeOnly(22, 0), new TimeOnly(23, 0));       // E3: no overlap
        }

        var reminderOnMiddleBlocked = await CreateTaskReminderAsync(middleBlockedId);
        var reminderOnFullyContained = await CreateTaskReminderAsync(fullyContainedId);

        var response = await ApplyAsync(calendarId, template.Id, "MergeOverwrite",
        [
            NewTask(activityId, 10, 0, 11, 0),  // blocker in the middle of E1
            NewTask(activityId, 14, 0, 15, 0)   // fully contains E2
        ]);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<ApplyTemplatePlannerTaskResponse>(JsonOpts, CancellationToken);

        body!.Tasks.Select(t => (t.StartTime.Hours, t.StartTime.Minutes, t.EndTime.Hours, t.EndTime.Minutes))
            .Should().BeEquivalentTo(
            [
                (9, 0, 10, 0),   // E1 segment 1
                (11, 0, 12, 0),  // E1 segment 2
                (22, 0, 23, 0),  // E3 untouched
                (10, 0, 11, 0),  // new blocker itself
                (14, 0, 15, 0)   // new task that swallowed E2
                // E2 gone entirely -- fully inside the new task
            ]);

        await using (var db = CreateDbContext())
        {
            (await db.Set<PlannerTask>().IgnoreQueryFilters().AnyAsync(t => t.Id == middleBlockedId, CancellationToken)).Should().BeFalse();
            (await db.Set<PlannerTask>().IgnoreQueryFilters().AnyAsync(t => t.Id == fullyContainedId, CancellationToken)).Should().BeFalse();
            (await db.Set<PlannerTask>().IgnoreQueryFilters().AnyAsync(t => t.Id == untouchedId, CancellationToken)).Should().BeTrue();
        }

        await AssertReminderCancelledAsync(reminderOnMiddleBlocked);
        await AssertReminderCancelledAsync(reminderOnFullyContained);
        await AssertTemplateStampAsync(template.Id, calendarId, expectedUsageCount: 1);
    }

    // ─── helpers ─────────────────────────────────────────────────────────────

    private async Task<(long ActivityId, TaskPlannerDayTemplate Template)> SeedApplyFixtureAsync(string tag)
    {
        await using var db = CreateDbContext();
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, $"apply-{tag}");

        var template = new TaskPlannerDayTemplate
        {
            UserId = UserId,
            Name = $"Apply template {tag}",
            IsActive = true,
            SuggestedForDayType = AdhdTimeOrganizer.Core.domain.model.@enum.DayType.Workday,
            DefaultWakeUpTime = new TimeOnly(6, 45),
            DefaultBedTime = new TimeOnly(22, 30)
        };
        db.Set<TaskPlannerDayTemplate>().Add(template);
        await db.SaveChangesAsync(CancellationToken);

        return (activityId, template);
    }

    private async Task<long> SeedCalendarAsync(DateOnly date)
    {
        await using var db = CreateDbContext();
        return await PlanningTestSeedHelper.SeedCalendarAsync(db, date);
    }

    private static object NewTask(long activityId, int startH, int startM, int endH, int endM) => new
    {
        StartTime = new { Hours = startH, Minutes = startM },
        EndTime = new { Hours = endH, Minutes = endM },
        IsBackground = false,
        ActivityId = activityId,
        Status = "NotStarted"
    };

    private Task<HttpResponseMessage> ApplyAsync(long calendarId, long templateId, string conflictResolution, object[] tasksFromTemplate) =>
        CreateClient().PostAsJsonAsync("api/calendar/apply-planner-template", new
        {
            CalendarId = calendarId,
            TemplateId = templateId,
            ConflictResolution = conflictResolution,
            TasksFromTemplate = tasksFromTemplate
        }, JsonOpts, CancellationToken);

    private async Task<long> CreateTaskReminderAsync(long plannerTaskId)
    {
        var response = await CreateClient().PostAsJsonAsync("api/reminder", new
        {
            Title = "Reminder to be orphaned",
            RemindAt = DateTime.UtcNow.AddHours(2),
            LeadOffsetsMinutes = new[] { 0 },
            PlannerTaskId = plannerTaskId
        }, JsonOpts, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return await response.Content.ReadFromJsonAsync<long>(CancellationToken);
    }

    private async Task AssertReminderCancelledAsync(long reminderId)
    {
        await using var db = CreateDbContext();
        var definition = await db.Set<ReminderDefinition>()
            .Where(d => d.OwnerModule == ReminderRegistrationService.OwnerModule
                        && d.SubjectType == ReminderRegistrationService.SubjectType
                        && d.SubjectId == reminderId.ToString()
                        && d.Kind == ReminderRegistrationService.Kind)
            .SingleAsync(CancellationToken);

        definition.Status.Should().Be(ReminderStatus.Cancelled,
            "the orphaning task delete happened inside ApplyTemplate, and the endpoint must cancel it just like DeletePlannerTaskEndpoint does");
        definition.NextOccurrenceAt.Should().BeNull();
    }

    private async Task AssertTemplateStampAsync(long templateId, long calendarId, int expectedUsageCount)
    {
        await using var db = CreateDbContext();

        var template = await db.Set<TaskPlannerDayTemplate>().SingleAsync(t => t.Id == templateId, CancellationToken);
        template.UsageCount.Should().Be(expectedUsageCount);
        template.LastUsedAt.Should().NotBeNull();
        template.LastUsedAt!.Value.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));

        var calendar = await db.Set<Calendar>().SingleAsync(c => c.Id == calendarId, CancellationToken);
        calendar.AppliedTemplateId.Should().Be(templateId);
        calendar.AppliedTemplateName.Should().Be(template.Name);
        calendar.WakeUpTime.Should().Be(template.DefaultWakeUpTime);
        calendar.BedTime.Should().Be(template.DefaultBedTime);
    }
}
