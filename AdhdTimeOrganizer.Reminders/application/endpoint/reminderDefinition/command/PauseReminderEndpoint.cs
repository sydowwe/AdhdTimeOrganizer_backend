using AdhdTimeOrganizer.Reminders.application.dto.reminderDefinition;
using FastEndpoints;
using MojaDigitalnaFirma.Kernel.reminders;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.Reminders.application.endpoint.reminderDefinition.command;

/// <summary>Pause a reminder (no occurrences dispatch until resumed). Idempotent. Open to any signed-in user (User/Admin/Root).</summary>
public class PauseReminderEndpoint(IReminderRegistry registry) : Endpoint<ReminderKeyRequest>
{
    public override void Configure()
    {
        Post("/reminder-definition/pause");
        Roles(this.GetUserRole());
        Summary(s =>
        {
            s.Summary = "Pause a reminder by its key";
            s.Response(204, "Paused (idempotent)");
        });
    }

    public override async Task HandleAsync(ReminderKeyRequest req, CancellationToken ct)
    {
        await registry.PauseAsync(req.ToKey(), ct);
        await Send.NoContentAsync(ct);
    }
}