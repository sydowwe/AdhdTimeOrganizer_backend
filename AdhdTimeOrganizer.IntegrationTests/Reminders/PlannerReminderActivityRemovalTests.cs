using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.IntegrationTests.Endpoints;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Planning.application.service.reminder;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Sydowwe.Reminders.domain.entity;
using Sydowwe.Reminders.domain.@enum;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Reminders;

/// <summary>
/// A personal reminder attached to a planner task goes with the task when the task's activity is deleted — the
/// <c>Reminder</c> row by cascade, its registration through <c>PlannerTaskActivityRemovalParticipant</c>. A merge
/// repoints the task instead, so its reminder must stay registered.
/// </summary>
[Collection("Postgres")]
public class PlannerReminderActivityRemovalTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    private static DateOnly TaskDay => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3);

    [Fact(DisplayName = "Deleting an activity cancels the reminders of the planner tasks its cascade removes")]
    public async Task DeleteActivity_CancelsTheTaskReminders()
    {
        var client = CreateClient();
        var (activityId, reminderId) = await SeedTaskWithReminderAsync(client);

        (await client.DeleteAsync($"api/activity/{activityId}", CancellationToken)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await DefinitionAsync(reminderId)).Status.Should().Be(ReminderStatus.Cancelled);
    }

    [Fact(DisplayName = "Merging an activity away keeps its repointed tasks' reminders registered")]
    public async Task Merge_KeepsTheRepointedTaskReminders()
    {
        var client = CreateClient();
        var (mergedId, reminderId) = await SeedTaskWithReminderAsync(client);
        long survivorId;
        await using (var db = CreateDbContext())
            survivorId = await PlanningTestSeedHelper.SeedActivityAsync(db, "merge-survivor", UserId, CancellationToken);

        var response = await client.PostAsJsonAsync("api/activity/merge",
            new { survivorId, mergedIds = new[] { mergedId } }, JsonOpts, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(CancellationToken));

        (await DefinitionAsync(reminderId)).Status.Should().Be(ReminderStatus.Active);
    }

    private async Task<(long ActivityId, long ReminderId)> SeedTaskWithReminderAsync(HttpClient client)
    {
        long activityId, taskId;
        await using (var db = CreateDbContext())
        {
            var task = await ReminderSeedHelper.SeedPlannerTaskAsync(db, UserId, TaskDay, new TimeOnly(12, 0), ct: CancellationToken);
            activityId = task.ActivityId;
            taskId = task.Id;
        }

        var created = await client.PostAsJsonAsync("api/reminder", new
        {
            Title = "Stand-up",
            RemindAt = DateTime.UtcNow.AddDays(3),
            LeadOffsetsMinutes = new[] { 0 },
            PlannerTaskId = taskId
        }, JsonOpts, CancellationToken);
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(CancellationToken));
        var reminderId = await created.Content.ReadFromJsonAsync<long>(CancellationToken);

        (await DefinitionAsync(reminderId)).Status.Should().Be(ReminderStatus.Active, "the precondition: it was registered");
        return (activityId, reminderId);
    }

    private async Task<ReminderDefinition> DefinitionAsync(long reminderId)
    {
        await using var db = CreateDbContext();
        return await db.Set<ReminderDefinition>().AsNoTracking().IgnoreQueryFilters()
            .SingleAsync(d => d.OwnerModule == ReminderRegistrationService.OwnerModule
                              && d.SubjectType == ReminderRegistrationService.SubjectType
                              && d.SubjectId == reminderId.ToString()
                              && d.Kind == ReminderRegistrationService.Kind, CancellationToken);
    }
}
