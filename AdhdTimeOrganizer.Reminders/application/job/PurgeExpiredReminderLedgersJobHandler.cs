using AdhdTimeOrganizer.Reminders.domain.entity;
using AdhdTimeOrganizer.Reminders.domain.@enum;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MojaDigitalnaFirma.Kernel.scheduling;
using Sydowwe.Framework.config.dependencyInjection;

namespace AdhdTimeOrganizer.Reminders.application.job;

// GDPR Art. 5(1)(e) (storage limitation) — retention for the Reminders module's append-only ledgers
// (audit 2026-07 finding L1 / idea B2, owner decision Q1). reminder_dispatch and
// reminder_occurrence_action are insert-only by design and would otherwise grow for the life of the
// deployment, carrying recipient user ids and template/payload snapshots forever. They are technical
// ledgers of the same class as scheduled_job_run and audit_log, so they share the 3-year default horizon
// (RetentionOptions.DefaultRetentionYears), configurable via ReminderRetentionOptions.
//
// The module dogfoods its own substrate: this is a second handler on the Scheduler substrate alongside
// the scan, registered through the Kernel IScheduler contract — Reminders still references no Quartz.
//
// ── Delete ordering is load-bearing ────────────────────────────────────────────────────────────────
// Every FK into these tables is DeleteBehavior.Restrict (the ledger invariant: history is never
// cascade-erased with its definition). A naive bulk delete therefore aborts the whole batch with an FK
// violation. The three passes below run in dependency order, each excluding rows that are still
// referenced:
//
//   1. reminder_dispatch — deleted FIRST, because it is the only table that points at the other two.
//      Guard: never delete a dispatch that another dispatch still reverses (the ReversesDispatchId
//      self-FK). Excluding every referenced row — not just those referenced by survivors — keeps the
//      single statement FK-safe even for reversal-of-a-reversal chains; a chain's tail becomes
//      unreferenced as its heads age out and is collected on a later run.
//   2. reminder_occurrence_action — only now can a snooze marker go, since the dispatch that fulfilled
//      it (ReminderOccurrenceActionId) may have just been removed by pass 1. Same self-FK guard for the
//      ReversesActionId reversal lineage.
//   3. reminder_definition — only Cancelled/Completed rows whose terminal instant is past the cutoff,
//      and only once NOTHING in either ledger still points at them. Active/Paused definitions are never
//      deleted regardless of age: they are live schedule state, not history.
//
// Passes 1 and 2 are each ONE ExecuteDeleteAsync: the age gate, a keep-last-N-per-definition floor (so a
// yearly reminder never loses its whole visible history to age alone), and that pass's FK guards. Kept as
// plain LINQ rather than routed through a shared generic helper — the age+floor half is four readable
// lines, and the FK guards, which are the part that actually differs per ledger, can't be shared anyway.
// The retention *policy* is shared (RetentionOptions); the queries are not. Both tables are [NoAudit], so
// the set-based delete loses nothing — and a retention purge must not write audit rows describing the
// data it just erased.
//
// Pass 3 is deliberately NOT set-based: ReminderDefinition is an audited entity, so its rows are loaded
// and removed through the ChangeTracker and the deletion lands in audit_log like any other. (PayloadJson
// is [AuditIgnore], so the snapshot carries no owner-supplied free-text PII.) Recipients and lead offsets
// cascade with the definition by configuration. Volume is low by construction — long-terminal rows only.
//
// The windows are operational policy, not statutory figures — deliberately NOT listed in
// docs/routineLawCheckups.md. Background-safe (no authenticated user); only counts are logged, never
// recipient ids or payloads.
//
// Per-user erasure (reminders audit L2) is a DIFFERENT axis and lives elsewhere: this purge deletes by
// AGE, while an Art. 17 erasure must reach one subject's rows ON DEMAND. See
// application/service/ReminderSubjectDataEraser.cs, reached through the Kernel ISubjectDataEraser
// fan-out so EmployeeModule never references this module.
public class PurgeExpiredReminderLedgersJobHandler(
    DbContext dbContext,
    IOptions<ReminderRetentionOptions> options,
    ILogger<PurgeExpiredReminderLedgersJobHandler> logger) : IScheduledJobHandler, IScopedService
{
    public const string HandlerKey = "Reminders.PurgeExpiredDispatchLog";

    public string Key => HandlerKey;

    public Task ExecuteAsync(ScheduledJobContext context, CancellationToken ct) => PurgeExpiredAsync(ct);

    public async Task PurgeExpiredAsync(CancellationToken ct)
    {
        var retention = options.Value;
        if (!retention.Enabled)
        {
            logger.LogDebug("Reminders ledger retention purge is disabled by configuration; skipping.");
            return;
        }

        var cutoff = retention.CutoffOffset();

        var dispatchesDeleted = await PurgeDispatchesAsync(retention, cutoff, ct);
        var actionsDeleted = await PurgeOccurrenceActionsAsync(retention, cutoff, ct);
        var definitionsDeleted = retention.PurgeTerminalDefinitions
            ? await PurgeTerminalDefinitionsAsync(cutoff, ct)
            : 0;

        if (dispatchesDeleted > 0 || actionsDeleted > 0 || definitionsDeleted > 0)
            logger.LogInformation(
                "Reminders retention purge: deleted {DispatchCount} dispatch row(s), {ActionCount} occurrence-action row(s) "
                + "and {DefinitionCount} terminal definition(s) past {Years}y retention (keep-last-{KeepLast}-per-definition floor)",
                dispatchesDeleted, actionsDeleted, definitionsDeleted, retention.RetentionYears, retention.KeepLastN);
    }

    /// <summary>Pass 1 — the dispatch ledger, guarded against the <c>ReversesDispatchId</c> self-FK.</summary>
    private Task<int> PurgeDispatchesAsync(ReminderRetentionOptions retention, DateTimeOffset cutoff, CancellationToken ct)
    {
        var keepLastN = retention.KeepLastN;
        var dispatches = dbContext.Set<ReminderDispatch>();

        return dispatches
            .Where(d => d.DispatchedAt < cutoff)
            // Keep-last-N floor: deletable only when at least N dispatches of the same definition are
            // strictly newer. A DispatchedAt tie counts as "not newer", erring on keeping rows.
            .Where(d => dispatches.Count(newer => newer.ReminderDefinitionId == d.ReminderDefinitionId
                                                  && newer.DispatchedAt > d.DispatchedAt) >= keepLastN)
            .Where(d => !dispatches.Any(other => other.ReversesDispatchId == d.Id))
            .ExecuteDeleteAsync(ct);
    }

    /// <summary>
    /// Pass 2 — the per-recipient snooze/dismiss ledger. Runs after pass 1 so markers whose fulfilling
    /// dispatch just aged out become collectable in the same run.
    /// </summary>
    private Task<int> PurgeOccurrenceActionsAsync(ReminderRetentionOptions retention, DateTimeOffset cutoff, CancellationToken ct)
    {
        var keepLastN = retention.KeepLastN;
        var actions = dbContext.Set<ReminderOccurrenceAction>();
        var dispatches = dbContext.Set<ReminderDispatch>();

        return actions
            .Where(a => a.ActedAt < cutoff)
            .Where(a => actions.Count(newer => newer.ReminderDefinitionId == a.ReminderDefinitionId
                                               && newer.ActedAt > a.ActedAt) >= keepLastN)
            .Where(a => !actions.Any(other => other.ReversesActionId == a.Id))
            .Where(a => !dispatches.Any(dispatch => dispatch.ReminderOccurrenceActionId == a.Id))
            .ExecuteDeleteAsync(ct);
    }

    /// <summary>
    /// Pass 3 — long-terminal definitions, once both ledgers are clear of them. Tracked (not set-based)
    /// so the deletion is audited; <c>Active</c>/<c>Paused</c> rows are never eligible.
    /// </summary>
    private async Task<int> PurgeTerminalDefinitionsAsync(DateTimeOffset cutoff, CancellationToken ct)
    {
        var dispatches = dbContext.Set<ReminderDispatch>();
        var actions = dbContext.Set<ReminderOccurrenceAction>();
        // A cancelled definition never gets a CompletedAt, so its age falls back to the last write —
        // the instant it was cancelled, since nothing touches a terminal row afterwards.
        var modifiedCutoff = cutoff.UtcDateTime;

        var expired = await dbContext.Set<ReminderDefinition>()
            .Where(definition => definition.Status == ReminderStatus.Cancelled || definition.Status == ReminderStatus.Completed)
            .Where(definition => definition.CompletedAt != null
                ? definition.CompletedAt < cutoff
                : definition.ModifiedTimestamp < modifiedCutoff)
            .Where(definition => !dispatches.Any(dispatch => dispatch.ReminderDefinitionId == definition.Id))
            .Where(definition => !actions.Any(action => action.ReminderDefinitionId == definition.Id))
            .ToListAsync(ct);

        if (expired.Count == 0)
            return 0;

        dbContext.RemoveRange(expired);
        await dbContext.SaveChangesAsync(ct);
        return expired.Count;
    }
}