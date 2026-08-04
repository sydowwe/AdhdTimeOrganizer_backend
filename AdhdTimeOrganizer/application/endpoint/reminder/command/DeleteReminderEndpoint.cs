using AdhdTimeOrganizer.domain.model.entity.reminder;
using AdhdTimeOrganizer.domain.serviceContract;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Sydowwe.Framework.application.endpoint.@base.command;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.application.endpoint.reminder.command;

public class DeleteReminderEndpoint(AppDbContext dbContext, IReminderRegistrationService reminders)
    : BaseDeleteEndpoint<Reminder>(dbContext)
{
    /// <summary>
    /// Defence in depth, and today unreachable — the global user query filter answers 404 first. See the note
    /// on <c>UpdateReminderEndpoint</c>.
    /// </summary>
    protected override Task<bool> AuthorizeAsync(Reminder entity, CancellationToken ct = default) => Task.FromResult(entity.UserId == User.GetId());

    /// <summary>
    /// Deleting the row is not enough — the module keeps its own <c>ReminderDefinition</c> and would keep
    /// firing a reminder whose portal row is gone. Withdrawing it is what the registry's cancel is for, and it
    /// runs after the delete commits so a failed delete leaves the schedule intact.
    /// </summary>
    protected override Task AfterSave(Reminder entity, CancellationToken ct = default) => reminders.CancelAsync(entity.Id, ct);
}
