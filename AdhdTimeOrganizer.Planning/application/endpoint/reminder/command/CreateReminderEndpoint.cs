using AdhdTimeOrganizer.Planning.application.dto.request.reminder;
using AdhdTimeOrganizer.Planning.application.validator;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.model.entity.reminder;
using AdhdTimeOrganizer.Planning.domain.serviceContract;
using Sydowwe.Framework.application.endpoint.@base.command;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.Planning.application.endpoint.reminder.command;

public class CreateReminderEndpoint(DbContext dbContext, IReminderRegistrationService reminders)
    : BaseCreateEndpoint<Reminder, ReminderRequest>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Validator<ReminderValidator>();
    }

    /// <summary>
    /// Ownership check for the optional planner-task link. <c>AppDbContext</c> applies a user query filter to
    /// every <c>IEntityWithUser</c>, so another user's task simply is not in this set — which makes "does it
    /// exist" and "is it mine" the same question, answered with one query.
    /// </summary>
    protected override async Task<bool> AfterMapping(Reminder entity, ReminderRequest req, CancellationToken ct = default)
    {
        if (req.PlannerTaskId is not { } plannerTaskId)
            return true;

        if (!await dbContext.Set<PlannerTask>().AnyAsync(t => t.Id == plannerTaskId, ct))
        {
            AddError(r => r.PlannerTaskId, "Planner task not found.");
            await Send.ErrorsAsync(404, ct);
            return false;
        }

        // UserId is stamped during SaveChanges, so it is still 0 here — the defaults lookup is keyed by user,
        // so give it the caller now rather than reading a zero.
        entity.UserId = User.GetId();
        await reminders.ApplyUserDefaultsAsync(entity, req.LeadOffsetsMinutes is not null, ct);
        return true;
    }

    /// <summary>
    /// Register only once the row is committed — the registry writes through the same context and commits, so
    /// a schedule published before the reminder existed could outlive a failed insert.
    /// </summary>
    protected override Task AfterSave(Reminder entity, CancellationToken ct = default) => reminders.SyncAsync(entity, ct);
}