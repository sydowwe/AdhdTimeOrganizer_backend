using System.Text.Json;
using AdhdTimeOrganizer.Reminders.domain.entity;
using AdhdTimeOrganizer.Reminders.domain.@enum;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.Contracts.gdpr;
using Sydowwe.Framework.Contracts.reminders;

namespace AdhdTimeOrganizer.Reminders.application.service;

// Reminders' recipient-side slice of a GDPR Art. 17 erasure (reminders audit 2026-07, finding L2 —
// reminders follow-up 03). Reached through the Sydowwe.Framework.Contracts ISubjectDataEraser fan-out, so EmployeeModule keeps
// no reference to this module. Mutates the ambient DbContext and never commits — the caller owns the
// transaction (see the contract's XML doc).
//
// ── Why this is mostly pseudonymize, not delete ─────────────────────────────────────────────────────
// reminder_dispatch and reminder_occurrence_action are APPEND-ONLY ledgers with Restrict FKs, and the
// scanner's no-double-send dedup reads them: the (ReminderDefinitionId, OccurrenceAt) dispatch index and
// the marker-state link ReminderDispatch.ReminderOccurrenceActionId. Deleting ledger rows would re-open
// occurrences the scanner already sent (double-send) and orphan reversal lineage. So the subject's OWN
// settings and recipient membership go, while the evidence that something happened stays with the
// identifier removed:
//
//   ReminderRecipient        → DELETE. Cascade-owned by its definition, no ledger invariant. An anonymized
//                              person is not a recipient. (Tracked Remove — the entity is audited.)
//   ReminderKindPreference   → DELETE. Per-user settings, meaningless once the subject is gone.
//   ReminderOccurrenceAction → PSEUDONYMIZE UserId to ErasedUserSentinel. The row is evidence a
//                              snooze/dismiss happened and the dispatch ledger may point at it
//                              (ReminderOccurrenceActionId, Restrict). Its index
//                              (ReminderDefinitionId, OccurrenceAt, UserId) is NON-UNIQUE — see
//                              ReminderOccurrenceActionEntityConfiguration — so collapsing several erased
//                              users onto one sentinel violates nothing.
//   ReminderDispatch         → SCRUB the id out of the RecipientsSnapshot jsonb array. Documented as
//                              "user ids only, no PII", but an id still identifies. Row and array shape kept.
//
// OUT OF SCOPE, deliberately: ReminderDefinition.PayloadJson and ReminderDefinition.SubjectId are
// SUBJECT-side, not recipient-side — a definition is *about* someone and is held on behalf of its
// recipients. The payload is opaque owner-supplied jsonb ([AuditIgnore] for exactly that reason) and only
// its producing module knows its shape, so guessing at it here would corrupt live reminders. Recorded as a
// known residual in the module review; tightening payloads belongs to the cross-module payload contract
// (prompts/payload-pii-contract.md), not here.
//
// PII discipline: only ids and counts are logged, never payloads or template text.
public class ReminderSubjectDataEraser(
    DbContext dbContext,
    ILogger<ReminderSubjectDataEraser> logger) : ISubjectDataEraser, IScopedService
{
    /// <summary>
    /// The pseudonym written over an erased recipient's <c>UserId</c> in the append-only action ledger.
    /// Zero is already the module-wide "no real user" sentinel (the value the user-scoped write path guards
    /// against), so it can never collide with a live recipient.
    /// </summary>
    public const long ErasedUserSentinel = 0;

    public string ModuleKey => "Reminders";

    public async Task<int> EraseSubjectDataAsync(SubjectErasureRequest request, CancellationToken ct)
    {
        var userId = request.UserId;
        if (userId == ErasedUserSentinel)
            return 0; // no real login — nothing in this module can be keyed to them

        var (removedRecipients, touchedDefinitionIds) = await EraseRecipientsAsync(userId, ct);
        var cancelledDefinitions = await CancelOrphanedDefinitionsAsync(userId, touchedDefinitionIds, ct);
        var removedPreferences = await ErasePreferencesAsync(userId, ct);
        var pseudonymizedActions = await PseudonymizeOccurrenceActionsAsync(userId, ct);
        var scrubbedSnapshots = await ScrubDispatchSnapshotsAsync(userId, ct);

        var total = removedRecipients + cancelledDefinitions + removedPreferences
                    + pseudonymizedActions + scrubbedSnapshots;

        if (total > 0)
            logger.LogInformation(
                "Reminders subject erasure for employee {EmployeeId}: removed {Recipients} recipient row(s) "
                + "and {Preferences} kind preference(s), cancelled {Definitions} orphaned definition(s), "
                + "pseudonymized {Actions} occurrence action(s) and scrubbed {Snapshots} dispatch snapshot(s)",
                request.EmployeeId, removedRecipients, removedPreferences, cancelledDefinitions,
                pseudonymizedActions, scrubbedSnapshots);

        return total;
    }

    /// <summary>
    /// Drops the subject's explicit-recipient membership. Tracked <c>RemoveRange</c> (the entity is audited,
    /// unlike the ledgers) — never <c>ExecuteDeleteAsync</c>. Returns the affected definition ids so the
    /// orphan check below does not have to re-derive them from the ChangeTracker.
    /// </summary>
    private async Task<(int Removed, IReadOnlyCollection<long> TouchedDefinitionIds)> EraseRecipientsAsync(
        long userId, CancellationToken ct)
    {
        var recipients = await dbContext.Set<ReminderRecipient>()
            .Where(r => r.UserId == userId)
            .ToListAsync(ct);
        if (recipients.Count == 0)
            return (0, []);

        dbContext.Set<ReminderRecipient>().RemoveRange(recipients);
        return (recipients.Count, recipients.Select(r => r.ReminderDefinitionId).Distinct().ToList());
    }

    /// <summary>
    /// Cancels any <c>ExplicitUsers</c> definition the erasure just left with no recipients at all.
    /// <para>
    /// Without this the scan would keep waking such a definition every occurrence, forever, only to append a
    /// <c>Skipped</c>/<c>NoRecipients</c> row (see <c>ReminderScanJobHandler.ResolveRecipientsAsync</c>) —
    /// not a silent failure, but unbounded ledger churn on a reminder nobody can receive. <c>Cancelled</c>
    /// (not deleted) is the right terminal state: the registry keeps the row so a re-register reactivates it
    /// with a fresh recipient set, and the retention purge collects it once it ages out. <c>Paused</c> /
    /// <c>Completed</c> rows are left alone — they are already not scanning.
    /// </para>
    /// <para>
    /// The recipient deletes are still pending (uncommitted), so "does anyone else remain?" is asked as
    /// <c>UserId != erased subject</c> rather than by counting rows — the query would otherwise still see the
    /// rows this pass is about to remove.
    /// </para>
    /// </summary>
    private async Task<int> CancelOrphanedDefinitionsAsync(
        long userId, IReadOnlyCollection<long> touchedDefinitionIds, CancellationToken ct)
    {
        if (touchedDefinitionIds.Count == 0)
            return 0;

        var stillHaveRecipients = await dbContext.Set<ReminderRecipient>()
            .Where(r => touchedDefinitionIds.Contains(r.ReminderDefinitionId) && r.UserId != userId)
            .Select(r => r.ReminderDefinitionId)
            .Distinct()
            .ToListAsync(ct);

        var orphanedIds = touchedDefinitionIds.Except(stillHaveRecipients).ToList();
        if (orphanedIds.Count == 0)
            return 0;

        var orphaned = await dbContext.Set<ReminderDefinition>()
            .Where(d => orphanedIds.Contains(d.Id)
                        && d.RecipientMode == RecipientMode.ExplicitUsers
                        && d.Status == ReminderStatus.Active)
            .ToListAsync(ct);
        if (orphaned.Count == 0)
            return 0;

        foreach (var definition in orphaned)
        {
            definition.Status = ReminderStatus.Cancelled;
            definition.IsActive = false;
            definition.NextOccurrenceAt = null;
        }

        logger.LogInformation(
            "Cancelled {Count} reminder definition(s) left with no recipients by a subject erasure", orphaned.Count);
        return orphaned.Count;
    }

    /// <summary>Drops the subject's per-kind opt-outs. Tracked <c>RemoveRange</c> so the caller stays in control of the commit.</summary>
    private async Task<int> ErasePreferencesAsync(long userId, CancellationToken ct)
    {
        var preferences = await dbContext.Set<ReminderKindPreference>()
            .Where(p => p.UserId == userId)
            .ToListAsync(ct);
        if (preferences.Count == 0)
            return 0;

        dbContext.Set<ReminderKindPreference>().RemoveRange(preferences);
        return preferences.Count;
    }

    /// <summary>
    /// Overwrites the subject's id in the append-only snooze/dismiss ledger with
    /// <see cref="ErasedUserSentinel"/>. The rows survive: they are the evidence an action happened, a
    /// dispatch may reference them through a Restrict FK, and the reversal lineage must stay intact.
    /// </summary>
    private async Task<int> PseudonymizeOccurrenceActionsAsync(long userId, CancellationToken ct)
    {
        var actions = await dbContext.Set<ReminderOccurrenceAction>()
            .Where(a => a.UserId == userId)
            .ToListAsync(ct);
        if (actions.Count == 0)
            return 0;

        foreach (var action in actions)
            action.UserId = ErasedUserSentinel;

        return actions.Count;
    }

    /// <summary>
    /// Removes the subject's id from every dispatch row's <c>RecipientsSnapshot</c> jsonb array, keeping the
    /// row and the array shape (an empty array is the module's normal "sent to nobody" value).
    /// <para>
    /// Candidates are narrowed in SQL with the jsonb containment operator (<c>@&gt;</c>) so the whole
    /// append-only ledger is never materialised; the rewrite itself is done in memory because it is a
    /// per-row array edit.
    /// </para>
    /// </summary>
    private async Task<int> ScrubDispatchSnapshotsAsync(long userId, CancellationToken ct)
    {
        var contained = $"[{userId}]";

        var candidates = await dbContext.Set<ReminderDispatch>()
            .Where(d => EF.Functions.JsonContains(d.RecipientsSnapshot, contained))
            .ToListAsync(ct);
        if (candidates.Count == 0)
            return 0;

        var scrubbed = 0;
        foreach (var dispatch in candidates)
        {
            var remaining = ParseRecipientIds(dispatch.RecipientsSnapshot)
                .Where(id => id != userId)
                .ToList();

            var rewritten = remaining.Count == 0 ? "[]" : JsonSerializer.Serialize(remaining);
            if (rewritten == dispatch.RecipientsSnapshot)
                continue;

            dispatch.RecipientsSnapshot = rewritten;
            scrubbed++;
        }

        return scrubbed;
    }

    /// <summary>
    /// Reads a <c>RecipientsSnapshot</c> array. Tolerates a malformed/legacy value by treating it as empty —
    /// an erasure must never abort on a bad ledger row, and the fallback errs toward removing the id.
    /// </summary>
    private List<long> ParseRecipientIds(string snapshot)
    {
        try
        {
            return JsonSerializer.Deserialize<List<long>>(snapshot) ?? [];
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Unparseable RecipientsSnapshot encountered during subject erasure; treating it as empty.");
            return [];
        }
    }
}