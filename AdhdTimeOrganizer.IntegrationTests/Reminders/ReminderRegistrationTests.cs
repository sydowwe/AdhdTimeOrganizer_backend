using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.application.service.reminder;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.reminder;
using AdhdTimeOrganizer.domain.model.@enum;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sydowwe.Framework.Contracts.notification;
using Sydowwe.Framework.Contracts.scheduling;
using Sydowwe.Framework.Testing;
using Sydowwe.Notifications.application;
using Sydowwe.Notifications.domain.entity;
using Sydowwe.Notifications.infrastructure.email;
using Sydowwe.Reminders.application.job;
using Sydowwe.Reminders.domain.entity;
using Sydowwe.Reminders.domain.@enum;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Reminders;

/// <summary>
/// What the portal's reminder feature is actually for: a reminder the user set reaches them as a
/// notification, an edit moves it rather than duplicating it, deleting or finishing the thing withdraws it,
/// and none of it happens to a planner task the user never asked to be reminded about.
/// <para>
/// These drive the Reminders scan handler directly from a controlled fire instant rather than waiting on
/// Quartz — the handler takes "now" from <see cref="ScheduledJobContext.ActualFireTimeUtc"/> precisely so a
/// reminder registered for the future can be fired past on demand. Registering something already overdue is
/// a deliberate no-op (the occurrence calculator never returns a past instant), so that is not a shortcut
/// available here.
/// </para>
/// </summary>
[Collection("Postgres")]
public class ReminderRegistrationTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long TestUserId = FakeLoggedUserService.TestUserId;

    /// <summary>
    /// The fixture sets every MAIL_* var, so SMTP auto-detects as configured and the dispatcher would open a
    /// socket to a fake host. PersonalReminder is email-default-OFF anyway — this pins the master switch so a
    /// future preference row can't turn these tests into network calls.
    /// </summary>
    private static readonly KeyValuePair<string, string?>[] EmailOff =
        [new($"{EmailNotificationOptions.SectionName}:Enabled", "false")];

    // ---- the end-to-end path ---------------------------------------------------------------------

    [Fact(DisplayName = "A standalone reminder the user created dispatches into notification history")]
    public async Task StandaloneReminder_FiresAndWritesNotificationAndDispatchRow()
    {
        var remindAt = DateTime.UtcNow.AddMinutes(30);

        await using var factory = CreateFactory(TestRoles.AdminAndUser, configOverrides: EmailOff);
        var response = await factory.CreateClient().PostAsJsonAsync("api/reminder", new
        {
            Title = "Call the dentist",
            RemindAt = remindAt,
            LeadOffsetsMinutes = new[] { 0 }
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var reminderId = await response.Content.ReadFromJsonAsync<long>(CancellationToken);

        await RunScanAsync(factory, remindAt.AddMinutes(1));

        await using var db = CreateDbContext();

        var definition = await SingleDefinitionAsync(db, reminderId);
        definition.Status.Should().Be(ReminderStatus.Completed, "a one-shot with a single offset has nothing left to fire");

        var dispatch = await db.Set<ReminderDispatch>().SingleAsync(d => d.ReminderDefinitionId == definition.Id, CancellationToken);
        dispatch.Outcome.Should().Be(DispatchOutcome.Sent);
        dispatch.NotificationTypeSnapshot.Should().Be(NotificationType.PersonalReminder);

        var notification = await db.Set<Notification>().SingleAsync(CancellationToken);
        notification.UserId.Should().Be(TestUserId);
        notification.Type.Should().Be(NotificationType.PersonalReminder);
        notification.IsRead.Should().BeFalse();
    }

    [Fact(DisplayName = "The user's own title reaches the notification body")]
    public async Task DispatchedReminder_RendersTheUsersOwnTitle()
    {
        var remindAt = DateTime.UtcNow.AddMinutes(30);

        await using var factory = CreateFactory(TestRoles.AdminAndUser, configOverrides: EmailOff);
        var response = await factory.CreateClient().PostAsJsonAsync("api/reminder", new
        {
            Title = "Take the bins out",
            RemindAt = remindAt,
            LeadOffsetsMinutes = new[] { 0 }
        }, JsonOpts, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        await RunScanAsync(factory, remindAt.AddMinutes(1));

        await using var db = CreateDbContext();
        var notification = await db.Set<Notification>().SingleAsync(CancellationToken);

        // Title/body are not stored — they are rendered from Type + PayloadJson at read time — so the whole
        // round trip is only proved by running the payload back through the real renderer.
        await using var scope = factory.Services.CreateAsyncScope();
        var rendered = scope.ServiceProvider.GetRequiredService<INotificationTextRenderer>()
            .Render(notification.Type, notification.PayloadJson);

        rendered.Body.Should().Contain("Take the bins out");
    }

    // ---- idempotency ------------------------------------------------------------------------------

    [Fact(DisplayName = "Editing the time re-registers in place — same definition, no duplicate")]
    public async Task EditingTheTime_ReRegistersInPlace()
    {
        await using var factory = CreateFactory(TestRoles.AdminAndUser, configOverrides: EmailOff);
        var client = factory.CreateClient();

        var created = await client.PostAsJsonAsync("api/reminder", new
        {
            Title = "Call the dentist",
            RemindAt = DateTime.UtcNow.AddHours(2),
            LeadOffsetsMinutes = new[] { 0 }
        }, JsonOpts, CancellationToken);
        var reminderId = await created.Content.ReadFromJsonAsync<long>(CancellationToken);

        long definitionIdBefore;
        await using (var before = CreateDbContext())
            definitionIdBefore = (await SingleDefinitionAsync(before, reminderId)).Id;

        var movedTo = DateTime.UtcNow.AddHours(5);
        var updated = await client.PutAsJsonAsync($"api/reminder/{reminderId}", new
        {
            Title = "Call the dentist",
            RemindAt = movedTo,
            LeadOffsetsMinutes = new[] { 0 }
        }, JsonOpts, CancellationToken);
        updated.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = CreateDbContext();

        // One row, the same row: RegisterAsync is an upsert keyed by the 4-tuple, so an edit must never leave
        // a second definition behind still pointing at the old time.
        var definitions = await AllDefinitionsAsync(db, reminderId);
        definitions.Should().ContainSingle();
        definitions[0].Id.Should().Be(definitionIdBefore);
        definitions[0].Status.Should().Be(ReminderStatus.Active);
        definitions[0].DueAt.Should().BeCloseTo(new DateTimeOffset(movedTo, TimeSpan.Zero), TimeSpan.FromSeconds(1));
    }

    [Fact(DisplayName = "Deleting the reminder cancels it in the module")]
    public async Task DeletingTheReminder_CancelsTheDefinition()
    {
        await using var factory = CreateFactory(TestRoles.AdminAndUser, configOverrides: EmailOff);
        var client = factory.CreateClient();

        var created = await client.PostAsJsonAsync("api/reminder", new
        {
            Title = "Call the dentist",
            RemindAt = DateTime.UtcNow.AddHours(2),
            LeadOffsetsMinutes = new[] { 0 }
        }, JsonOpts, CancellationToken);
        var reminderId = await created.Content.ReadFromJsonAsync<long>(CancellationToken);

        var deleted = await client.DeleteAsync($"api/reminder/{reminderId}", CancellationToken);
        deleted.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var db = CreateDbContext();

        // The definition survives the portal row — it is the ledger's anchor — but it must be withdrawn, or
        // the scan keeps firing a reminder whose owner is gone.
        var definition = await SingleDefinitionAsync(db, reminderId);
        definition.Status.Should().Be(ReminderStatus.Cancelled);
        definition.NextOccurrenceAt.Should().BeNull();
    }

    // ---- planner-task linkage ---------------------------------------------------------------------

    [Fact(DisplayName = "A planner task with no reminder attached produces nothing — reminders are opt-in")]
    public async Task PlannerTaskWithoutAReminder_NeverNotifies()
    {
        await using var factory = CreateFactory(TestRoles.AdminAndUser, configOverrides: EmailOff);

        await using (var seed = CreateDbContext())
            await ReminderSeedHelper.SeedPlannerTaskAsync(
                seed, TestUserId, DateOnly.FromDateTime(DateTime.UtcNow), new TimeOnly(9, 0), ct: CancellationToken);

        await RunScanAsync(factory, DateTime.UtcNow.AddHours(1));

        await using var db = CreateDbContext();
        (await db.Set<ReminderDefinition>().CountAsync(CancellationToken)).Should().Be(0, "no reminder row means no registration");
        (await db.Set<Notification>().CountAsync(CancellationToken)).Should().Be(0);
    }

    [Fact(DisplayName = "A task-linked reminder takes its instant from the task's day and start time")]
    public async Task TaskLinkedReminder_DerivesItsInstantFromTheTask()
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);
        var startTime = new TimeOnly(9, 0);

        await using var factory = CreateFactory(TestRoles.AdminAndUser, configOverrides: EmailOff);

        long taskId;
        await using (var seed = CreateDbContext())
            taskId = (await ReminderSeedHelper.SeedPlannerTaskAsync(seed, TestUserId, date, startTime, ct: CancellationToken)).Id;

        // RemindAt is deliberately sent as something else entirely: for a task-linked reminder it is derived,
        // not authored, so the registration service must overwrite whatever the client sent.
        var created = await factory.CreateClient().PostAsJsonAsync("api/reminder", new
        {
            Title = "Stand-up",
            RemindAt = DateTime.UtcNow.AddDays(9),
            LeadOffsetsMinutes = new[] { -10, 0 },
            PlannerTaskId = taskId
        }, JsonOpts, CancellationToken);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var reminderId = await created.Content.ReadFromJsonAsync<long>(CancellationToken);

        // The fixture's test user sits in UTC, so the expected instant is the plain composition.
        var expected = date.ToDateTime(startTime);

        await using var db = CreateDbContext();
        var reminder = await db.Set<Reminder>().SingleAsync(r => r.Id == reminderId, CancellationToken);
        reminder.RemindAt.Should().BeCloseTo(expected, TimeSpan.FromSeconds(1));

        var definition = await SingleDefinitionAsync(db, reminderId);
        definition.DueAt.Should().BeCloseTo(new DateTimeOffset(expected, TimeSpan.Zero), TimeSpan.FromSeconds(1));
        definition.LeadOffsets.Select(o => o.OffsetMinutes).Should().BeEquivalentTo([-10, 0]);
    }

    [Fact(DisplayName = "Moving the task's start time re-registers the reminder at the new instant")]
    public async Task MovingTheTask_ReRegistersTheReminder()
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);

        await using var factory = CreateFactory(TestRoles.AdminAndUser, configOverrides: EmailOff);
        var client = factory.CreateClient();

        long taskId;
        await using (var seed = CreateDbContext())
            taskId = (await ReminderSeedHelper.SeedPlannerTaskAsync(seed, TestUserId, date, new TimeOnly(9, 0), ct: CancellationToken)).Id;

        var created = await client.PostAsJsonAsync("api/reminder", new
        {
            Title = "Stand-up",
            RemindAt = DateTime.UtcNow.AddDays(1),
            LeadOffsetsMinutes = new[] { 0 },
            PlannerTaskId = taskId
        }, JsonOpts, CancellationToken);
        var reminderId = await created.Content.ReadFromJsonAsync<long>(CancellationToken);

        var moved = await client.PatchAsJsonAsync($"api/planner-task/{taskId}", new
        {
            StartTime = new { Hours = 14, Minutes = 30 },
            EndTime = new { Hours = 15, Minutes = 30 }
        }, JsonOpts, CancellationToken);
        moved.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var db = CreateDbContext();
        var definitions = await AllDefinitionsAsync(db, reminderId);
        definitions.Should().ContainSingle("re-registration is an upsert, not a second definition");
        definitions[0].DueAt.Should().BeCloseTo(
            new DateTimeOffset(date.ToDateTime(new TimeOnly(14, 30)), TimeSpan.Zero), TimeSpan.FromSeconds(1));
    }

    [Theory(DisplayName = "Finishing or calling off a task retires its reminder")]
    [InlineData(PlannerTaskStatus.Completed)]
    [InlineData(PlannerTaskStatus.Cancelled)]
    public async Task FinishingTheTask_CancelsItsReminder(PlannerTaskStatus status)
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);

        await using var factory = CreateFactory(TestRoles.AdminAndUser, configOverrides: EmailOff);
        var client = factory.CreateClient();

        long taskId;
        await using (var seed = CreateDbContext())
            taskId = (await ReminderSeedHelper.SeedPlannerTaskAsync(seed, TestUserId, date, new TimeOnly(9, 0), ct: CancellationToken)).Id;

        var created = await client.PostAsJsonAsync("api/reminder", new
        {
            Title = "Stand-up",
            RemindAt = DateTime.UtcNow.AddDays(1),
            LeadOffsetsMinutes = new[] { 0 },
            PlannerTaskId = taskId
        }, JsonOpts, CancellationToken);
        var reminderId = await created.Content.ReadFromJsonAsync<long>(CancellationToken);

        var patched = await client.PatchAsJsonAsync($"api/planner-task/{taskId}/status", new { Status = status.ToString() }, JsonOpts, CancellationToken);
        patched.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var db = CreateDbContext();
        var definition = await SingleDefinitionAsync(db, reminderId);
        definition.Status.Should().Be(ReminderStatus.Cancelled);
        definition.NextOccurrenceAt.Should().BeNull();
    }

    [Fact(DisplayName = "Deleting the task cancels the reminder the FK cascade takes with it")]
    public async Task DeletingTheTask_CancelsItsReminder()
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);

        await using var factory = CreateFactory(TestRoles.AdminAndUser, configOverrides: EmailOff);
        var client = factory.CreateClient();

        long taskId;
        await using (var seed = CreateDbContext())
            taskId = (await ReminderSeedHelper.SeedPlannerTaskAsync(seed, TestUserId, date, new TimeOnly(9, 0), ct: CancellationToken)).Id;

        var created = await client.PostAsJsonAsync("api/reminder", new
        {
            Title = "Stand-up",
            RemindAt = DateTime.UtcNow.AddDays(1),
            LeadOffsetsMinutes = new[] { 0 },
            PlannerTaskId = taskId
        }, JsonOpts, CancellationToken);
        var reminderId = await created.Content.ReadFromJsonAsync<long>(CancellationToken);

        var deleted = await client.DeleteAsync($"api/planner-task/{taskId}", CancellationToken);
        deleted.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var db = CreateDbContext();

        // The portal row went with the task by cascade; the module row must have been withdrawn explicitly,
        // because no cascade reaches across the module boundary.
        (await db.Set<Reminder>().CountAsync(r => r.Id == reminderId, CancellationToken)).Should().Be(0);
        (await SingleDefinitionAsync(db, reminderId)).Status.Should().Be(ReminderStatus.Cancelled);
    }

    [Fact(DisplayName = "Omitting the lead offsets on a task reminder takes UserPlannerSettings.ReminderMinutesBefore")]
    public async Task TaskReminderWithoutOffsets_TakesTheUsersConfiguredLeadTime()
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);

        await using var factory = CreateFactory(TestRoles.AdminAndUser, configOverrides: EmailOff);

        long taskId;
        await using (var seed = CreateDbContext())
        {
            seed.Set<UserPlannerSettings>().Add(new UserPlannerSettings
            {
                UserId = TestUserId, RemindersEnabled = true, ReminderMinutesBefore = 25
            });
            await seed.SaveChangesAsync(CancellationToken);
            taskId = (await ReminderSeedHelper.SeedPlannerTaskAsync(seed, TestUserId, date, new TimeOnly(9, 0), ct: CancellationToken)).Id;
        }

        // LeadOffsetsMinutes deliberately absent — that is the "use my default" signal.
        var created = await factory.CreateClient().PostAsJsonAsync("api/reminder", new
        {
            Title = "Stand-up",
            RemindAt = DateTime.UtcNow.AddDays(1),
            PlannerTaskId = taskId
        }, JsonOpts, CancellationToken);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var reminderId = await created.Content.ReadFromJsonAsync<long>(CancellationToken);

        await using var db = CreateDbContext();
        var reminder = await db.Set<Reminder>().SingleAsync(r => r.Id == reminderId, CancellationToken);
        reminder.LeadOffsetsMinutes.Should().BeEquivalentTo([-25], "the setting is minutes *before*, so it lands as a negative offset");

        (await SingleDefinitionAsync(db, reminderId)).LeadOffsets.Select(o => o.OffsetMinutes).Should().BeEquivalentTo([-25]);
    }

    [Fact(DisplayName = "RemindersEnabled=false suppresses the prefill, it never drops a reminder the user created")]
    public async Task RemindersDisabled_StillCreatesTheReminderWithoutTheLeadDefault()
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);

        await using var factory = CreateFactory(TestRoles.AdminAndUser, configOverrides: EmailOff);

        long taskId;
        await using (var seed = CreateDbContext())
        {
            seed.Set<UserPlannerSettings>().Add(new UserPlannerSettings
            {
                UserId = TestUserId, RemindersEnabled = false, ReminderMinutesBefore = 25
            });
            await seed.SaveChangesAsync(CancellationToken);
            taskId = (await ReminderSeedHelper.SeedPlannerTaskAsync(seed, TestUserId, date, new TimeOnly(9, 0), ct: CancellationToken)).Id;
        }

        var created = await factory.CreateClient().PostAsJsonAsync("api/reminder", new
        {
            Title = "Stand-up",
            RemindAt = DateTime.UtcNow.AddDays(1),
            PlannerTaskId = taskId
        }, JsonOpts, CancellationToken);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var reminderId = await created.Content.ReadFromJsonAsync<long>(CancellationToken);

        await using var db = CreateDbContext();
        var reminder = await db.Set<Reminder>().SingleAsync(r => r.Id == reminderId, CancellationToken);
        reminder.LeadOffsetsMinutes.Should().BeEquivalentTo([0], "no lead prefill, but it still fires at the task's start");

        (await SingleDefinitionAsync(db, reminderId)).Status.Should().Be(ReminderStatus.Active);
    }

    // ---- validation --------------------------------------------------------------------------------

    [Fact(DisplayName = "A one-shot reminder in the past is refused rather than silently never firing")]
    public async Task PastOneShotReminder_IsRejected()
    {
        var response = await CreateClient().PostAsJsonAsync("api/reminder", new
        {
            Title = "Yesterday's problem",
            RemindAt = DateTime.UtcNow.AddHours(-1),
            LeadOffsetsMinutes = new[] { 0 }
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact(DisplayName = "A positive lead offset is refused — '10 minutes before' is -10")]
    public async Task PositiveLeadOffset_IsRejected()
    {
        var response = await CreateClient().PostAsJsonAsync("api/reminder", new
        {
            Title = "Call the dentist",
            RemindAt = DateTime.UtcNow.AddHours(2),
            LeadOffsetsMinutes = new[] { 10 }
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact(DisplayName = "A yearly reminder anchored in the past is accepted — that is what a birthday is")]
    public async Task PastAnchoredRecurringReminder_IsAccepted()
    {
        await using var factory = CreateFactory(TestRoles.AdminAndUser, configOverrides: EmailOff);
        var response = await factory.CreateClient().PostAsJsonAsync("api/reminder", new
        {
            Title = "Birthday",
            RemindAt = DateTime.UtcNow.AddYears(-30),
            LeadOffsetsMinutes = new[] { 0 },
            Recurrence = nameof(ReminderRecurrence.Yearly)
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var reminderId = await response.Content.ReadFromJsonAsync<long>(CancellationToken);

        await using var db = CreateDbContext();
        var definition = await SingleDefinitionAsync(db, reminderId);
        definition.Status.Should().Be(ReminderStatus.Active);
        definition.NextOccurrenceAt.Should().NotBeNull("the calculator walks a past anchor forward to the next occurrence");
        definition.NextOccurrenceAt!.Value.Should().BeAfter(DateTimeOffset.UtcNow);
    }

    // ---- IDOR --------------------------------------------------------------------------------------

    [Fact(DisplayName = "Another user's reminder is neither readable, editable nor deletable")]
    public async Task OtherUsersReminder_IsUnreachable()
    {
        long otherReminderId;
        await using (var seed = CreateDbContext())
        {
            await ReminderSeedHelper.EnsureOtherUserAsync(seed, CancellationToken);
            otherReminderId = (await ReminderSeedHelper.SeedReminderAsync(
                seed, ReminderSeedHelper.OtherUserId, DateTime.UtcNow.AddHours(2), "Not yours", ct: CancellationToken)).Id;
        }

        var client = CreateClient();

        // 404 on all three, not 403: AppDbContext's global user query filter removes the row from every
        // lookup — the read projection and the write path's FindAsync alike — so the endpoints never see it
        // and the ownership hooks never get a chance to run. That is the outcome we want; a 403 would confirm
        // to an attacker that the id is real. The AuthorizeAsync overrides on the write endpoints stay as
        // defence in depth for the day someone excludes this entity from the filter.
        (await client.GetAsync($"api/reminder/{otherReminderId}", CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await client.PutAsJsonAsync($"api/reminder/{otherReminderId}", new
        {
            Title = "Hijacked",
            RemindAt = DateTime.UtcNow.AddHours(3),
            LeadOffsetsMinutes = new[] { 0 }
        }, JsonOpts, CancellationToken)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await client.DeleteAsync($"api/reminder/{otherReminderId}", CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using var db = CreateDbContext();
        var survivor = await db.Set<Reminder>().IgnoreQueryFilters()
            .SingleAsync(r => r.Id == otherReminderId, CancellationToken);
        survivor.Title.Should().Be("Not yours");
    }

    [Fact(DisplayName = "A reminder cannot be attached to another user's planner task")]
    public async Task AttachingToAnotherUsersTask_IsRefused()
    {
        long otherTaskId;
        await using (var seed = CreateDbContext())
        {
            await ReminderSeedHelper.EnsureOtherUserAsync(seed, CancellationToken);
            otherTaskId = (await ReminderSeedHelper.SeedPlannerTaskAsync(
                seed, ReminderSeedHelper.OtherUserId, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1),
                new TimeOnly(9, 0), ct: CancellationToken)).Id;
        }

        var response = await CreateClient().PostAsJsonAsync("api/reminder", new
        {
            Title = "Snooping",
            RemindAt = DateTime.UtcNow.AddHours(2),
            LeadOffsetsMinutes = new[] { 0 },
            PlannerTaskId = otherTaskId
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- helpers -----------------------------------------------------------------------------------

    /// <summary>
    /// Fires the Reminders scan at a chosen instant. "Now" comes from the fire time, not the wall clock, so a
    /// reminder registered for the future can be driven past deterministically.
    /// </summary>
    private async Task RunScanAsync(ITestClientFactory factory, DateTime fireTimeUtc)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var scan = scope.ServiceProvider.GetServices<IScheduledJobHandler>()
            .Single(h => h.Key == ReminderScanJobHandler.HandlerKey);

        var fireTime = DateTime.SpecifyKind(fireTimeUtc, DateTimeKind.Utc);
        await scan.ExecuteAsync(new ScheduledJobContext
        {
            ScheduledFireTimeUtc = fireTime,
            ActualFireTimeUtc = fireTime,
            JobKey = ReminderScanJobHandler.HandlerKey,
            CorrelationId = "portal-reminder-test",
            TriggerSource = TriggerSource.Manual
        }, CancellationToken);
    }

    private Task<ReminderDefinition> SingleDefinitionAsync(DbContext db, long reminderId) =>
        DefinitionQuery(db, reminderId).SingleAsync(CancellationToken);

    private Task<List<ReminderDefinition>> AllDefinitionsAsync(DbContext db, long reminderId) =>
        DefinitionQuery(db, reminderId).ToListAsync(CancellationToken);

    private static IQueryable<ReminderDefinition> DefinitionQuery(DbContext db, long reminderId) =>
        db.Set<ReminderDefinition>()
            .Include(d => d.LeadOffsets)
            .Where(d => d.OwnerModule == ReminderRegistrationService.OwnerModule
                        && d.SubjectType == ReminderRegistrationService.SubjectType
                        && d.SubjectId == reminderId.ToString()
                        && d.Kind == ReminderRegistrationService.Kind);
}