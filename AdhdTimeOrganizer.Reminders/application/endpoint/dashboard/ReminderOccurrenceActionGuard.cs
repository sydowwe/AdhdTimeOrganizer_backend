using AdhdTimeOrganizer.Reminders.domain.entity;
using Microsoft.EntityFrameworkCore;
using MojaDigitalnaFirma.Kernel.reminders;

namespace AdhdTimeOrganizer.Reminders.application.endpoint.dashboard;

/// <summary>
/// Shared self-scoping guard for the snooze / dismiss endpoints (phase 05b). A user may act <b>only</b> on an
/// occurrence they are an explicit recipient of — resolver-strategy reminders are excluded (their recipients are
/// unknown until dispatch and a request path must never invoke a resolver). The caller sees a uniform "not found"
/// for a missing definition, a resolver-strategy definition, or one they are not a recipient of, so the endpoint
/// never leaks whether a definition exists (IDOR).
/// </summary>
internal static class ReminderOccurrenceActionGuard
{
    /// <summary>
    /// Outcome of <see cref="ResolveRecipientOccurrenceAsync"/>. <see cref="IsRecipient"/> is false for a
    /// missing/resolver-strategy definition or a non-recipient (→ 404); when true,
    /// <see cref="NextOccurrenceAt"/> is the definition's current shared next occurrence — the <b>only</b>
    /// occurrence instant the dashboard ever surfaces to a recipient, and therefore the only one a
    /// snooze/dismiss may legitimately target.
    /// </summary>
    public readonly record struct RecipientOccurrence(bool IsRecipient, DateTimeOffset? NextOccurrenceAt);

    /// <summary>
    /// Resolves, in one query, whether <paramref name="userId"/> is an explicit recipient of
    /// <paramref name="definitionId"/> and (if so) the definition's current <see cref="ReminderDefinition.NextOccurrenceAt"/>.
    /// Callers reject a request whose <c>OccurrenceAt</c> does not match the returned occurrence, so a recipient
    /// cannot snooze/dismiss a fabricated occurrence that was never scheduled.
    /// </summary>
    public static async Task<RecipientOccurrence> ResolveRecipientOccurrenceAsync(
        DbContext dbContext, long definitionId, long userId, CancellationToken ct)
    {
        var match = await dbContext.Set<ReminderDefinition>()
            .Where(d => d.Id == definitionId
                        && d.RecipientMode == RecipientMode.ExplicitUsers
                        && d.Recipients.Any(r => r.UserId == userId))
            .Select(d => new { d.NextOccurrenceAt })
            .FirstOrDefaultAsync(ct);

        return match is null
            ? new RecipientOccurrence(false, null)
            : new RecipientOccurrence(true, match.NextOccurrenceAt);
    }
}