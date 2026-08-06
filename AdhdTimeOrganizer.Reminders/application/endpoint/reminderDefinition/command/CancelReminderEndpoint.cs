using AdhdTimeOrganizer.Reminders.application.dto.reminderDefinition;
using FastEndpoints;
using MojaDigitalnaFirma.Kernel.reminders;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.Reminders.application.endpoint.reminderDefinition.command;

/// <summary>Withdraw the reminder for a key. Idempotent no-op on an unknown/already-cancelled key. Open to any signed-in user (User/Admin/Root).</summary>
public class CancelReminderEndpoint(IReminderRegistry registry) : Endpoint<ReminderKeyRequest>
{
    public override void Configure()
    {
        Post("/reminder-definition/cancel");
        Roles(this.GetUserRole());
        Summary(s =>
        {
            s.Summary = "Cancel (withdraw) a reminder by its key";
            s.Response(204, "Cancelled (idempotent)");
        });
    }

    public override async Task HandleAsync(ReminderKeyRequest req, CancellationToken ct)
    {
        await registry.CancelAsync(req.ToKey(), ct);
        await Send.NoContentAsync(ct);
    }
}