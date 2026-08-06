using AdhdTimeOrganizer.Reminders.application.dto.dashboard;
using AdhdTimeOrganizer.Reminders.domain.entity;
using AdhdTimeOrganizer.Reminders.domain.@enum;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.Reminders.application.endpoint.dashboard;

/// <summary>
/// Snooze one of the caller's <b>own</b> upcoming reminder occurrences (POST <c>/reminder-dashboard/snooze</c>),
/// deferring <i>their</i> delivery to <see cref="SnoozeReminderOccurrenceRequest.SnoozeUntil"/>. Append-only: it
/// writes a new <see cref="ReminderOccurrenceAction"/> row, never mutating the definition's shared
/// <c>NextOccurrenceAt</c> or any past dispatch row — so other recipients of the same occurrence are unaffected.
/// User role and up; self-scoped (only an explicit recipient of the definition may act — see
/// <see cref="ReminderOccurrenceActionGuard"/>).
/// </summary>
public class SnoozeReminderOccurrenceEndpoint(DbContext dbContext) : Endpoint<SnoozeReminderOccurrenceRequest>
{
    public override void Configure()
    {
        Post("/reminder-dashboard/snooze");
        Roles(this.GetUserRole());
        Summary(s =>
        {
            s.Summary = "Snooze one of my upcoming reminder occurrences";
            s.Response(204, "Snoozed (append-only)");
            s.Response(400, "OccurrenceAt is not the definition's pending occurrence, or SnoozeUntil is not in the future");
            s.Response(404, "Not a recipient of this reminder occurrence");
        });
    }

    public override async Task HandleAsync(SnoozeReminderOccurrenceRequest req, CancellationToken ct)
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
            AddError(r => r.OccurrenceAt, "Only an upcoming occurrence can be snoozed.");
        else if (req.OccurrenceAt != recipient.NextOccurrenceAt)
            AddError(r => r.OccurrenceAt, "OccurrenceAt must match this reminder's pending occurrence.");
        if (req.SnoozeUntil <= now)
            AddError(r => r.SnoozeUntil, "SnoozeUntil must be in the future.");

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
            ActionType = ReminderActionType.Snooze,
            SnoozeUntil = req.SnoozeUntil,
            ActedAt = now
        });
        await dbContext.SaveChangesAsync(ct);

        await Send.NoContentAsync(ct);
    }
}