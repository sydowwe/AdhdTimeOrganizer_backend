using AdhdTimeOrganizer.application.dto.request.reminder;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.domain.model.entity.reminder;
using AdhdTimeOrganizer.domain.serviceContract;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.command;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.application.endpoint.reminder.command;

public class UpdateReminderEndpoint(AppDbContext dbContext, IReminderRegistrationService reminders)
    : BaseUpdateEndpoint<Reminder, ReminderRequest>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Validator<ReminderValidator>();
    }

    /// <summary>
    /// Defence in depth, and today unreachable: <c>AppDbContext</c>'s global user query filter already removes
    /// another user's row from the base's lookup, so a foreign id answers 404 before this runs. Kept because
    /// the base's contract does not promise a filter — remove the filter from this entity and this becomes the
    /// only thing standing between two users' reminders.
    /// </summary>
    protected override Task<bool> AuthorizeAsync(Reminder entity, CancellationToken ct = default) => Task.FromResult(entity.UserId == User.GetId());

    protected override async Task<bool> AfterMapping(Reminder entity, ReminderRequest req, CancellationToken ct = default)
    {
        if (req.PlannerTaskId is not { } plannerTaskId)
            return true;

        if (!await dbContext.PlannerTasks.AnyAsync(t => t.Id == plannerTaskId, ct))
        {
            AddError(r => r.PlannerTaskId, "Planner task not found.");
            await Send.ErrorsAsync(404, ct);
            return false;
        }

        await reminders.ApplyUserDefaultsAsync(entity, req.LeadOffsetsMinutes is not null, ct);
        return true;
    }

    /// <summary>
    /// Re-registration is just another <c>RegisterAsync</c>: the registry upserts by key, so an edited time
    /// updates the same definition in place rather than leaving a second one behind.
    /// </summary>
    protected override Task AfterSave(Reminder entity, CancellationToken ct = default) => reminders.SyncAsync(entity, ct);
}
