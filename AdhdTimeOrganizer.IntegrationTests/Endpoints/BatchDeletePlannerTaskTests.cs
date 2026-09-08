using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.IntegrationTests.Reminders;
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
/// TEST-6 — <c>BatchDeletePlannerTaskEndpoint</c>. The single delete has three suites behind it; the batch
/// had none, and it carries the same capture-before-cascade logic:
/// <c>BeforeDeleteAsync</c> reads the attached reminder ids while the rows still exist and
/// <c>AfterSave</c> cancels them once the delete commits. The portal <c>Reminder</c> rows go with the tasks
/// by FK cascade, which the Reminders <b>module</b> never sees — so dropping <c>BeforeDeleteAsync</c> still
/// answers 204 while the module keeps firing reminders for tasks that no longer exist.
/// <c>BatchDelete_CancelsTheModuleRegistrationOfEveryReminderTheCascadeTakes</c> is the test that fails
/// if it is ever removed.
/// <para>
/// The base-behaviour half copies <c>BatchDeleteTemplatePlannerTaskEndpointTests</c> in
/// <c>PlanningCrudAuthMatrixTests</c>, including its cross-user 404-not-403 assertion.
/// </para>
/// </summary>
[Collection("Postgres")]
public class BatchDeletePlannerTaskTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const string Url = "api/planner-task/batch-delete";
    private const long UserId = FakeLoggedUserService.TestUserId;

    private static object Body(IEnumerable<long> ids) => new { ids };

    [Fact(DisplayName = "Batch-deleting the day's last tasks removes them all and leaves the calendar day standing")]
    public async Task BatchDelete_RemovesEveryTask_AndLeavesTheCalendarDayStanding()
    {
        long calendarId;
        long[] taskIds;
        await using (var db = CreateDbContext())
        {
            var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "planner-task-batchdelete", ct: CancellationToken);
            calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, new DateOnly(2027, 7, 1), ct: CancellationToken);
            taskIds = await SeedTasksAsync(db, activityId, calendarId, 3);
        }

        var response = await CreateUserRoleClient().PostAsJsonAsync(Url, Body(taskIds), JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var verifyDb = CreateDbContext();
        (await verifyDb.Set<PlannerTask>().IgnoreQueryFilters().CountAsync(t => taskIds.Contains(t.Id), CancellationToken))
            .Should().Be(0);

        // planner_task.calendar_id is Restrict, not Cascade: emptying a day must not disturb the day itself.
        (await verifyDb.Set<Calendar>().IgnoreQueryFilters().AnyAsync(c => c.Id == calendarId, CancellationToken))
            .Should().BeTrue("deleting the day's last task leaves the Calendar row alone");
    }

    /// <summary>
    /// The whole reason this suite exists. Both halves of the seam are asserted: the portal rows leave by
    /// cascade, and the module-side <c>ReminderDefinition</c> — which no cascade can reach — is withdrawn.
    /// </summary>
    [Fact(DisplayName = "Batch-deleting tasks cancels the module registration of every reminder the cascade takes with them")]
    public async Task BatchDelete_CancelsTheModuleRegistrationOfEveryReminderTheCascadeTakes()
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);

        long[] taskIds;
        await using (var db = CreateDbContext())
        {
            var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "planner-task-batchdelete-reminders", ct: CancellationToken);
            var calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, date, ct: CancellationToken);
            taskIds = await SeedTasksAsync(db, activityId, calendarId, 3);
        }

        // Only two of the three carry a reminder — the third proves the batch does not need one to succeed.
        var firstReminderId = await CreateTaskReminderAsync(CreateClient(), taskIds[0]);
        var secondReminderId = await CreateTaskReminderAsync(CreateClient(), taskIds[1]);

        var response = await CreateUserRoleClient().PostAsJsonAsync(Url, Body(taskIds), JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var verifyDb = CreateDbContext();
        (await verifyDb.Set<PlannerTask>().IgnoreQueryFilters().CountAsync(t => taskIds.Contains(t.Id), CancellationToken)).Should().Be(0);

        long[] reminderIds = [firstReminderId, secondReminderId];
        (await verifyDb.Set<Reminder>().IgnoreQueryFilters().CountAsync(r => reminderIds.Contains(r.Id), CancellationToken))
            .Should().Be(0, "PlannerTask -> Reminder is DeleteBehavior.Cascade");

        await AssertReminderCancelledAsync(firstReminderId);
        await AssertReminderCancelledAsync(secondReminderId);
    }

    /// <summary>
    /// <c>PlannerTask</c> is <c>IEntityWithUser</c>, so a foreign id is filtered out of the endpoint's own
    /// lookup and the batch takes the count-mismatch path — 404, never 403, exactly as the template sibling
    /// (<c>BatchDeleteTemplatePlannerTask_CrossUserId_Returns404NotForbidden</c>) records. The part that
    /// matters is what the 404 leaves behind: the batch is refused whole, not partially applied.
    /// </summary>
    [Fact(DisplayName = "A batch containing another user's task id 404s and deletes nothing — not even the caller's own tasks")]
    public async Task BatchDelete_WithAnotherUsersId_Returns404_AndDeletesNothing()
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);
        var otherUserId = ReminderSeedHelper.OtherUserId;

        long[] myTaskIds;
        long theirTaskId;
        await using (var db = CreateDbContext())
        {
            await ReminderSeedHelper.EnsureOtherUserAsync(db, CancellationToken);

            var myActivityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "planner-task-batchdelete-mine", ct: CancellationToken);
            var myCalendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, date, ct: CancellationToken);
            myTaskIds = await SeedTasksAsync(db, myActivityId, myCalendarId, 2);

            var theirActivityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "planner-task-batchdelete-theirs", otherUserId, CancellationToken);
            var theirCalendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, date, otherUserId, CancellationToken);
            theirTaskId = await PlanningTestSeedHelper.SeedPlannerTaskAsync(
                db, theirActivityId, theirCalendarId, new TimeOnly(9, 0), new TimeOnly(10, 0), otherUserId, ct: CancellationToken);
        }

        // Their task gets a reminder of its own, registered through their own client — a cancelled definition
        // is the damage that a batch reaching across the user boundary would do without touching a status code.
        long theirReminderId;
        await using (var theirFactory = CreateFactory(TestRoles.AdminAndUser, otherUserId))
            theirReminderId = await CreateTaskReminderAsync(theirFactory.CreateClient(), theirTaskId);

        var response = await CreateUserRoleClient()
            .PostAsJsonAsync(Url, Body([..myTaskIds, theirTaskId]), JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using var verifyDb = CreateDbContext();
        (await verifyDb.Set<PlannerTask>().IgnoreQueryFilters().AnyAsync(t => t.Id == theirTaskId, CancellationToken))
            .Should().BeTrue("the other user's task was never the caller's to delete");
        (await verifyDb.Set<PlannerTask>().IgnoreQueryFilters().CountAsync(t => myTaskIds.Contains(t.Id), CancellationToken))
            .Should().Be(myTaskIds.Length, "a 404 on the batch means nothing in it was touched, the caller's own rows included");

        await AssertReminderStillActiveAsync(theirReminderId);
    }

    /// <summary>
    /// Indistinguishable from the cross-user case above, which is the intended behaviour: a foreign id and an
    /// id that never existed both leave the batch one row short and answer the same 404.
    /// </summary>
    [Fact(DisplayName = "A batch containing an id that does not exist 404s and deletes nothing")]
    public async Task BatchDelete_WithAnUnknownId_Returns404_AndDeletesNothing()
    {
        long[] taskIds;
        await using (var db = CreateDbContext())
        {
            var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "planner-task-batchdelete-unknown", ct: CancellationToken);
            var calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, new DateOnly(2027, 7, 2), ct: CancellationToken);
            taskIds = await SeedTasksAsync(db, activityId, calendarId, 2);
        }

        var response = await CreateUserRoleClient()
            .PostAsJsonAsync(Url, Body([..taskIds, 99999999L]), JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using var verifyDb = CreateDbContext();
        (await verifyDb.Set<PlannerTask>().IgnoreQueryFilters().CountAsync(t => taskIds.Contains(t.Id), CancellationToken))
            .Should().Be(taskIds.Length);
    }

    /// <summary>
    /// The base rejects an empty list at the door with 400, so nothing downstream runs — the short-circuit in
    /// <c>ReminderRegistrationService.GetReminderIdsForPlannerTasksAsync</c> is never even reached.
    /// </summary>
    [Fact(DisplayName = "An empty id list is a 400, not a no-op 204")]
    public async Task BatchDelete_WithAnEmptyList_Returns400()
    {
        var response = await CreateUserRoleClient().PostAsJsonAsync(Url, Body([]), JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task BatchDelete_Unauthenticated_Returns401()
    {
        long[] taskIds;
        await using (var db = CreateDbContext())
        {
            var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "planner-task-batchdelete-anon", ct: CancellationToken);
            var calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, new DateOnly(2027, 7, 3), ct: CancellationToken);
            taskIds = await SeedTasksAsync(db, activityId, calendarId, 1);
        }

        var response = await CreateUnauthenticatedClient().PostAsJsonAsync(Url, Body(taskIds), JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        await using var verifyDb = CreateDbContext();
        (await verifyDb.Set<PlannerTask>().IgnoreQueryFilters().CountAsync(t => taskIds.Contains(t.Id), CancellationToken))
            .Should().Be(taskIds.Length);
    }

    // ─── helpers ─────────────────────────────────────────────────────────────

    private static async Task<long[]> SeedTasksAsync(DbContext db, long activityId, long calendarId, int count, long userId = UserId)
    {
        var ids = new long[count];
        for (var i = 0; i < count; i++)
            ids[i] = await PlanningTestSeedHelper.SeedPlannerTaskAsync(
                db, activityId, calendarId, new TimeOnly(9 + i, 0), new TimeOnly(9 + i, 45), userId, ct: CancellationToken);
        return ids;
    }

    /// <summary>
    /// Registered through the real endpoint rather than seeded: <c>ReminderSeedHelper.SeedReminderAsync</c>
    /// writes the portal row only, and a module-side definition that was never created cannot be observed to
    /// have been cancelled.
    /// </summary>
    private async Task<long> CreateTaskReminderAsync(HttpClient client, long plannerTaskId)
    {
        var response = await client.PostAsJsonAsync("api/reminder", new
        {
            Title = "Reminder the batch delete must withdraw",
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
        var definition = await DefinitionQuery(db, reminderId).SingleAsync(CancellationToken);

        definition.Status.Should().Be(ReminderStatus.Cancelled,
            "the FK cascade took the portal row, so only BeforeDeleteAsync/AfterSave can withdraw the module's copy");
        definition.NextOccurrenceAt.Should().BeNull();
    }

    private async Task AssertReminderStillActiveAsync(long reminderId)
    {
        await using var db = CreateDbContext();
        var definition = await DefinitionQuery(db, reminderId).SingleAsync(CancellationToken);

        definition.Status.Should().Be(ReminderStatus.Active, "the task it belongs to was never deleted");
        definition.NextOccurrenceAt.Should().NotBeNull();
    }

    private static IQueryable<ReminderDefinition> DefinitionQuery(DbContext db, long reminderId) =>
        db.Set<ReminderDefinition>()
            .Where(d => d.OwnerModule == ReminderRegistrationService.OwnerModule
                        && d.SubjectType == ReminderRegistrationService.SubjectType
                        && d.SubjectId == reminderId.ToString()
                        && d.Kind == ReminderRegistrationService.Kind);
}
