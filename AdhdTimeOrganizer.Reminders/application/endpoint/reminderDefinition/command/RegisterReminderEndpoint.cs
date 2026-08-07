using AdhdTimeOrganizer.Reminders.application.dto.reminderDefinition;
using FastEndpoints;
using Sydowwe.Framework.Contracts.notification.payload;
using Sydowwe.Framework.Contracts.reminders;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.Reminders.application.endpoint.reminderDefinition.command;

/// <summary>
/// Diagnostic / manual-ops register (idempotent upsert by the reminder key). Owning modules normally register
/// from their own code against <see cref="IReminderRegistry"/> directly — this endpoint is for ad-hoc admin
/// use. Open to any signed-in user (User/Admin/Root).
/// </summary>
public class RegisterReminderEndpoint(IReminderRegistry registry)
    : Endpoint<RegisterReminderRequest, ReminderRegistrationResult>
{
    public override void Configure()
    {
        Post("/reminder-definition/register");
        Roles(this.GetUserRole());
        Summary(s =>
        {
            s.Summary = "Register (idempotent upsert) a reminder by its key";
            s.Response<ReminderRegistrationResult>(200, "Registered (created or updated in place)");
            s.Response(400, "Invalid schedule, recipients, or text source");
        });
    }

    public override async Task HandleAsync(RegisterReminderRequest req, CancellationToken ct)
    {
        // Per-type required fields the flat→typed mapping would otherwise paper over (e.g. a one-shot with no
        // DueAt). The registry re-validates everything else (uniqueness, cron validity, resolver/renderer keys).
        switch (req.ScheduleType)
        {
            case ReminderScheduleType.OneShot when req.DueAt is null:
                AddError(r => r.DueAt, "A one-shot reminder requires DueAt.");
                break;
            case ReminderScheduleType.RecurringInterval when req.IntervalPreset is null || req.AnchorDate is null:
                AddError(r => r.AnchorDate, "A recurring-interval reminder requires an interval preset and an anchor date.");
                break;
            case ReminderScheduleType.RecurringCron when string.IsNullOrWhiteSpace(req.Cron):
                AddError(r => r.Cron, "A recurring-cron reminder requires a cron expression.");
                break;
        }

        // The payload PII contract, enforced at runtime because this is the one registration path the compiler
        // cannot police: module code registers a typed IReminderPayload record, but an admin posts free-form
        // JSON. Reject a person-data key here rather than persisting PII that outlives the subject's erasure.
        if (PayloadPersonDataNames.ContainsPersonData(req.Payload) is { } offendingPath)
            AddError(r => r.Payload,
                $"Payload property '{offendingPath}' looks like person data. A reminder payload carries ids and " +
                "non-person scalars only — the display name is resolved at render time.");

        if (ValidationFailed)
        {
            await Send.ErrorsAsync(400, ct);
            return;
        }

        try
        {
            var result = await registry.RegisterAsync(req.ToRegistration(), ct);
            await Send.OkAsync(result, ct);
        }
        catch (ArgumentException ex)
        {
            AddError(ex.Message);
            await Send.ErrorsAsync(400, ct);
        }
    }
}