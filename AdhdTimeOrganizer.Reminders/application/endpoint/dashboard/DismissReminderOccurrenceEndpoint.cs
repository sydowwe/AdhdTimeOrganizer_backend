using AdhdTimeOrganizer.Reminders.application.dto.dashboard;
using AdhdTimeOrganizer.Reminders.domain.entity;
using AdhdTimeOrganizer.Reminders.domain.@enum;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.Reminders.application.endpoint.dashboard;

/// <summary>
/// Dismiss one of the caller's <b>own</b> upcoming reminder occurrences (POST <c>/reminder-dashboard/dismiss</c>),
/// suppressing <i>their</i> delivery of it. Append-only: it writes a new <see cref="ReminderOccurrenceAction"/>
/// row, never mutating the definition's shared <c>NextOccurrenceAt</c> or any past dispatch row — so other
/// recipients of the same occurrence are unaffected (reversible only by a reversal row). User role and up;
/// self-scoped (only an explicit recipient of the definition may act — see
/// <see cref="ReminderOccurrenceActionGuard"/>).
/// </summary>
public class DismissReminderOccurrenceEndpoint(DbContext dbContext) : Endpoint<DismissReminderOccurrenceRequest>
{
    public override void Configure()
    {
        Post("/reminder-dashboard/dismiss");
        Roles(this.GetUserRole());
        Summary(s =>
        {
            s.Summary = "Dismiss one of my upcoming reminder occurrences";
            s.Response(204, "Dismissed (append-only)");
            s.Response(400, "OccurrenceAt is not the definition's pending occurrence");
            s.Response(404, "Not a recipient of this reminder occurrence");
        });
    }

    public override async Task HandleAsync(DismissReminderOccurrenceRequest req, CancellationToken ct)
    {
        var userId = User.GetId();

        var recipient = await ReminderOccurrenceActionGuard.ResolveRecipientOccurrenceAsync(dbContext, req.ReminderDefinitionId, userId, ct);
        if (!recipient.IsRecipient)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (req.OccurrenceAt <= now)
            AddError(r => r.OccurrenceAt, "Only an upcoming occurrence can be dismissed.");
        else if (req.OccurrenceAt != recipient.NextOccurrenceAt)
            AddError(r => r.OccurrenceAt, "OccurrenceAt must match this reminder's pending occurrence.");

        if (ValidationFailed)
        {
            await Send.ErrorsAsync(400, ct);
            return;
        }

        dbContext.Add(new ReminderOccurrenceAction
        {
            ReminderDefinitionId = req.ReminderDefinitionId,
            OccurrenceAt = req.OccurrenceAt,
            UserId = userId,
            ActionType = ReminderActionType.Dismiss,
            SnoozeUntil = null,
            ActedAt = now
        });
        await dbContext.SaveChangesAsync(ct);

        await Send.NoContentAsync(ct);
    }
}