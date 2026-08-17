using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// TEST-3 / Section G — delete behaviour for the planner FK graph, exercised directly through
/// <c>AppDbContext</c> rather than HTTP: there is no <c>DeleteCalendarEndpoint</c> at all, so the
/// Calendar→PlannerTask edge can only be reached by deleting the row directly (e.g. a future admin tool, a
/// retention job, or a manual op).
/// <para>
/// <b>These are the opposite of what <c>docs/domain-map.md</c> and the review's testing prompt both claim.</b>
/// The prompt says "Calendar → PlannerTask Cascade" and "Activity → PlannerTask Restrict". The actual model
/// (<c>PlannerTaskConfiguration.cs</c>, confirmed against <c>AppDbContextModelSnapshot.cs</c>) is the reverse:
/// </para>
/// <list type="bullet">
/// <item><c>PlannerTaskConfiguration.Configure</c> hand-sets <c>Calendar → PlannerTask</c> to
/// <b><c>DeleteBehavior.Restrict</c></b> — deleting a <c>Calendar</c> row that still has tasks is blocked at
/// the database, not cascaded.</item>
/// <item><c>PlannerTaskConfiguration.Configure</c> calls <c>IsManyWithOneActivity()</c> with no override, and
/// that extension's own default is <b><c>DeleteBehavior.Cascade</c></b> — deleting an <c>Activity</c> silently
/// deletes every <c>PlannerTask</c> that references it, including tasks the user has already completed.</item>
/// </list>
/// <para>
/// This is flagged as a finding, not adjusted to match the docs: tests below assert the <i>actual</i> database
/// behaviour, since that is what a caller experiences.
/// </para>
/// </summary>
[Collection("Postgres")]
public class PlannerDeleteBehaviorTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    [Fact(DisplayName = "FINDING: deleting a Calendar with tasks is Restricted (blocked), not Cascaded, contradicting docs/domain-map.md")]
    public async Task DeletingCalendarWithTasks_IsRestricted_NotCascaded()
    {
        long calendarId;
        // Seeded and committed through its OWN context, then discarded: the deleting context below must not
        // track the child PlannerTask, or EF's change tracker detects the severed-required-relationship
        // client-side (InvalidOperationException) before the delete ever reaches Postgres -- which would mean
        // this test isn't actually exercising the database-level Restrict constraint it claims to.
        await using (var seedDb = CreateDbContext())
        {
            var activityId = await PlanningTestSeedHelper.SeedActivityAsync(seedDb, "delete-calendar-restrict");
            calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(seedDb, new DateOnly(2027, 6, 1));
            await PlanningTestSeedHelper.SeedPlannerTaskAsync(seedDb, activityId, calendarId, new TimeOnly(9, 0), new TimeOnly(10, 0));
        }

        await using var db = CreateDbContext();
        var calendar = await db.Set<Calendar>().SingleAsync(c => c.Id == calendarId, CancellationToken);
        db.Set<Calendar>().Remove(calendar);

        var act = async () => await db.SaveChangesAsync(CancellationToken);

        // fk_planner_task_...calendar_id, DeleteBehavior.Restrict -> Postgres 23503 foreign_key_violation.
        var exception = await act.Should().ThrowAsync<DbUpdateException>();
        (exception.Which.InnerException as PostgresException)?.SqlState.Should().Be(PostgresErrorCodes.ForeignKeyViolation);

        await using var verifyDb = CreateDbContext();
        (await verifyDb.Set<Calendar>().IgnoreQueryFilters().AnyAsync(c => c.Id == calendarId, CancellationToken))
            .Should().BeTrue("the delete was blocked, so the row must still exist");
    }

    [Fact(DisplayName = "A Calendar day with no tasks can be deleted directly")]
    public async Task DeletingAnEmptyCalendar_Succeeds()
    {
        await using var db = CreateDbContext();
        var calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, new DateOnly(2027, 6, 2));

        var calendar = await db.Set<Calendar>().SingleAsync(c => c.Id == calendarId, CancellationToken);
        db.Set<Calendar>().Remove(calendar);
        await db.SaveChangesAsync(CancellationToken);

        await using var verifyDb = CreateDbContext();
        (await verifyDb.Set<Calendar>().IgnoreQueryFilters().AnyAsync(c => c.Id == calendarId, CancellationToken)).Should().BeFalse();
    }

    [Fact(DisplayName = "FINDING: deleting an Activity referenced by a PlannerTask cascades the task away, not a clean 409")]
    public async Task DeletingActivityReferencedByTask_Cascades_NotRestricted()
    {
        await using var db = CreateDbContext();
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "delete-activity-cascades");
        var calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, new DateOnly(2027, 6, 3));
        var taskId = await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, calendarId, new TimeOnly(9, 0), new TimeOnly(10, 0));

        var activity = await db.Set<Activity>().SingleAsync(a => a.Id == activityId, CancellationToken);
        db.Set<Activity>().Remove(activity);
        await db.SaveChangesAsync(CancellationToken);

        await using var verifyDb = CreateDbContext();
        (await verifyDb.Set<PlannerTask>().IgnoreQueryFilters().AnyAsync(t => t.Id == taskId, CancellationToken))
            .Should().BeFalse("Activity -> PlannerTask is DeleteBehavior.Cascade (IsManyWithOneActivity's default, never overridden here) " +
                               "-- a planned or already-completed task silently disappears with the activity, rather than blocking the delete " +
                               "with a 409 the way docs/domain-map.md's 'Activity -> PlannerTask Restrict' claims");
    }

    [Fact(DisplayName = "Deleting a TaskImportance referenced by a task SetNulls the reference rather than deleting the task")]
    public async Task DeletingTaskImportance_SetNullsTheReference()
    {
        await using var db = CreateDbContext();
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "delete-importance-setnull");
        var calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, new DateOnly(2027, 6, 4));
        var importanceId = await PlanningTestSeedHelper.SeedTaskImportanceAsync(db, 5);
        var taskId = await PlanningTestSeedHelper.SeedPlannerTaskAsync(
            db, activityId, calendarId, new TimeOnly(9, 0), new TimeOnly(10, 0), importanceId: importanceId);

        var importance = await db.Set<TaskImportance>().SingleAsync(t => t.Id == importanceId, CancellationToken);
        db.Set<TaskImportance>().Remove(importance);
        await db.SaveChangesAsync(CancellationToken);

        await using var verifyDb = CreateDbContext();
        var task = await verifyDb.Set<PlannerTask>().SingleAsync(t => t.Id == taskId, CancellationToken);
        task.ImportanceId.Should().BeNull("TaskImportance -> PlannerTask.ImportanceId is DeleteBehavior.SetNull");
    }

    [Fact(DisplayName = "Deleting a PlannerTask cascades its reminders")]
    public async Task DeletingPlannerTask_CascadesItsReminders()
    {
        await using var db = CreateDbContext();
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "delete-task-cascades-reminder");
        var calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, new DateOnly(2027, 6, 5));
        var taskId = await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, calendarId, new TimeOnly(9, 0), new TimeOnly(10, 0));

        var reminder = await AdhdTimeOrganizer.IntegrationTests.Reminders.ReminderSeedHelper
            .SeedReminderAsync(db, PlanningTestSeedHelper.TestUserId, DateTime.UtcNow.AddHours(2), plannerTaskId: taskId, ct: CancellationToken);
        var reminderId = reminder.Id;

        var task = await db.Set<PlannerTask>().SingleAsync(t => t.Id == taskId, CancellationToken);
        db.Set<PlannerTask>().Remove(task);
        await db.SaveChangesAsync(CancellationToken);

        await using var verifyDb = CreateDbContext();
        (await verifyDb.Set<AdhdTimeOrganizer.Planning.domain.model.entity.reminder.Reminder>()
                .IgnoreQueryFilters().AnyAsync(r => r.Id == reminderId, CancellationToken))
            .Should().BeFalse("PlannerTask -> Reminder is DeleteBehavior.Cascade");
    }
}
