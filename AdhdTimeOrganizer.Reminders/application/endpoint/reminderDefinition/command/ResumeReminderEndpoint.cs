using AdhdTimeOrganizer.Reminders.application.dto.reminderDefinition;
using FastEndpoints;
using MojaDigitalnaFirma.Kernel.reminders;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.Reminders.application.endpoint.reminderDefinition.command;

/// <summary>Resume a paused reminder (recomputes NextOccurrenceAt). Idempotent. Open to any signed-in user (User/Admin/Root).</summary>
public class ResumeReminderEndpoint(IReminderRegistry registry) : Endpoint<ReminderKeyRequest>
{
    public override void Configure()
    {
        Post("/reminder-definition/resume");
        Roles(IEndpoint.GetUserRole());
        Summary(s =>
        {
            s.Summary = "Resume a paused reminder by its key";
            s.Response(204, "Resumed (idempotent)");
        });
    }

    public override async Task HandleAsync(ReminderKeyRequest req, CancellationToken ct)
    {
        await registry.ResumeAsync(req.ToKey(), ct);
        await Send.NoContentAsync(ct);
    }
}