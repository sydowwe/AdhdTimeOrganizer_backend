using AdhdTimeOrganizer.Planning.application.dto.request.reminder;
using AdhdTimeOrganizer.Planning.application.validator;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.model.entity.reminder;
using AdhdTimeOrganizer.Planning.domain.serviceContract;
using Sydowwe.Framework.application.endpoint.@base.command;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.Planning.application.endpoint.reminder.command;

public class UpdateReminderEndpoint(DbContext dbContext, IReminderRegistrationService reminders)
    : BaseUpdateEndpoint<Reminder, ReminderRequest>(dbContext)
{
    private readonly DbContext _dbContext = dbContext;

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

    private bool _wasAuto;

    protected override async Task<bool> AfterMapping(Reminder entity, ReminderRequest req, CancellationToken ct = default)
    {
        // An automatic reminder the user edits becomes theirs: registered as a PersonalReminder from now on, never
        // held while they are away, and a sign they touched the block. It also cannot end up as an "automatic"
        // reminder detached from any block. The old key is withdrawn in AfterSave, once the row has committed.
        _wasAuto = entity.IsAuto;
        entity.IsAuto = false;

        if (req.PlannerTaskId is not { } plannerTaskId)
            return true;

        if (!await _dbContext.Set<PlannerTask>().AnyAsync(t => t.Id == plannerTaskId, ct))
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
    /// <para>
    /// A reminder that was automatic changes kind, and the kind is part of the key, so its automatic definition
    /// is cancelled first — otherwise it would fire alongside the re-registered one.
    /// </para>
    /// </summary>
    protected override async Task AfterSave(Reminder entity, CancellationToken ct = default)
    {
        if (_wasAuto)
            await reminders.CancelAsync(entity.Id, ct);

        await reminders.SyncAsync(entity, ct);
    }
}