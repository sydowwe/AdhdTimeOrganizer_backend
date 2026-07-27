using System.Diagnostics;
using System.Text.Json.Nodes;
using AdhdTimeOrganizer.Reminders.domain.entity;
using AdhdTimeOrganizer.Reminders.domain.@enum;
using AdhdTimeOrganizer.Reminders.domain.service;
using AdhdTimeOrganizer.Reminders.infrastructure.scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MojaDigitalnaFirma.Kernel.notification;
using MojaDigitalnaFirma.Kernel.notification.payload;
using MojaDigitalnaFirma.Kernel.reminders;
using MojaDigitalnaFirma.Kernel.scheduling;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.Reminders.application.job;

/// <summary>
/// The scan algorithm — the dispatch engine that turns the registry into actual sends. Implemented as a keyed
/// <see cref="IScheduledJobHandler"/> (key <see cref="HandlerKey"/>); the Scheduler module registers it as the
/// single recurring scan job in phase 03b (this module never owns a Quartz <c>IJob</c> or calls
/// <c>AddQuartz</c>). On each fire it finds due occurrences, dedupes them against the append-only
/// <see cref="ReminderDispatch"/> log, resolves recipients + text through the owner's Kernel strategies,
/// dispatches through the Notification module (<see cref="INotificationService.NotifyAsync"/>), appends a
/// dispatch row, and advances <see cref="ReminderDefinition.NextOccurrenceAt"/> via the shared
/// <see cref="ReminderOccurrenceCalculator"/> (so register/resume and the scan agree by construction).
/// <para>
/// <b>Idempotent by construction:</b> step 3's dedup means running the scan twice over the same window
/// produces no duplicate send. The check-then-insert is race-free only because 03b registers the job with
/// <c>DisallowConcurrent</c> (no two scans overlap; sequential re-runs see the prior run's committed rows) —
/// the non-unique <c>(ReminderDefinitionId, OccurrenceAt)</c> index supports the lookup but does not itself
/// prevent a concurrent double-insert.
/// </para>
/// <para>
/// <b>Catch-up (pinned default):</b> after downtime several instants may be due at once. The scan acts on the
/// <b>single most-recent due, undispatched occurrence per reminder per run</b> and advances
/// <c>NextOccurrenceAt</c> past all older missed instants (each collapsed to a <see cref="DispatchOutcome.Skipped"/>
/// row for audit) so the next scan cannot re-pick them. This bounds sends to one per reminder per scan — no
/// stale storm. The Quartz-level misfire policy is set on the registration in 03b; this is the per-occurrence
/// catch-up Reminders owns.
/// </para>
/// <para>
/// <b>No domain imports:</b> recipients and text come exclusively through the Kernel strategy abstractions the
/// owners registered — the scanner knows nothing domain-specific. Background-safe (no authenticated user): the
/// definitions are plain-table entities and the Notification contract is safe to call with no principal.
/// </para>
/// <para>
/// <b>Dispatch policy (phase 04a):</b> after resolving recipients, the per-kind opt-out
/// (<see cref="ReminderKindPreference"/>) drops muted recipients (all muted ⇒ a <see cref="SkipReason.OptedOut"/>
/// row) and quiet hours <i>defer</i> — not drop — the occurrence when every recipient is inside their window.
/// Absent any preference rows the behaviour is exactly phase 03 (one immediate send per occurrence): the policy
/// is strictly additive.
/// <br/>
/// The quiet-hours window itself is <b>not this module's</b>: notifications follow-up 05 consolidated it into
/// the Notifications module (one window per user, deployment-wide, also honoured by the notification
/// dispatcher), and the scan reads it through the Kernel <see cref="IQuietHoursReader"/> seam. The scan-side
/// deferral mechanism is unchanged — <c>NextOccurrenceAt</c> is left untouched and re-evaluated next scan, so
/// no deferred-delivery state is needed here.
/// </para>
/// <para>
/// <b>Digest batching (phase 04b):</b> a definition carrying a <see cref="ReminderDefinition.DigestKey"/> is
/// processed by <see cref="ProcessDigestKeyAsync"/> instead of one-send-per-occurrence: the key's definitions
/// are aggregated so each recipient gets <b>one</b> <see cref="NotificationType.ReminderDigest"/> notification
/// across every due definition sharing the key, while still writing <b>one per-occurrence dispatch row per
/// definition</b> (the audit trail is never collapsed). The batching window is configured per key in
/// <see cref="ReminderDigestOptions"/> — a key with no config flushes every run. Definitions with no
/// <c>DigestKey</c> are unchanged (strictly additive).
/// </para>
/// <para>
/// <b>Per-recipient snooze / dismiss (phase 05b):</b> at the resolve-recipients step
/// (<see cref="ApplyOccurrenceMarkersAsync"/>) a recipient with an effective <see cref="ReminderOccurrenceAction"/>
/// for the due occurrence is dropped — dismiss suppresses their delivery, snooze defers it — with a
/// per-recipient <see cref="DispatchOutcome.Skipped"/> row for audit; the shared <c>NextOccurrenceAt</c> and other
/// recipients are untouched. A second due-source (<see cref="ProcessDueSnoozesAsync"/>) re-delivers snoozes whose
/// <c>SnoozeUntil</c> has passed as single-recipient sends, deduped by the marker's own state (a marker-linked
/// dispatch row), never by the occurrence-keyed dispatch dedup. That re-delivery still honours the phase-04a
/// <b>per-kind opt-out</b> (a recipient who muted the kind after snoozing is dropped to an
/// <see cref="SkipReason.OptedOut"/> row) but deliberately ignores quiet hours — the user chose the
/// <c>SnoozeUntil</c> instant, so it is honoured even inside a quiet window.
/// </para>
/// </summary>
public sealed class ReminderScanJobHandler : IScheduledJobHandler, IScopedService
{
    private readonly DbContext dbContext;
    private readonly INotificationService notificationService;
    private readonly IEnumerable<IReminderRecipientResolver> recipientResolvers;
    private readonly IEnumerable<IReminderRenderer> renderers;
    private readonly ICronEvaluator cronEvaluator;
    private readonly IQuietHoursReader quietHoursReader;
    private readonly ReminderDigestOptions digestOptions;
    private readonly ILogger<ReminderScanJobHandler> logger;

    /// <summary>
    /// The deployment-configured time zone (<c>Application:Timezone</c>) quiet-hours windows are interpreted in.
    /// The repo models no per-user time zone; this single deployment zone is the one <c>WorkHourCalculatorService</c>
    /// uses. Falls back to UTC if the config is missing/invalid so a misconfiguration never crashes the scan.
    /// </summary>
    private readonly TimeZoneInfo timeZone;

    public ReminderScanJobHandler(
        DbContext dbContext,
        INotificationService notificationService,
        IEnumerable<IReminderRecipientResolver> recipientResolvers,
        IEnumerable<IReminderRenderer> renderers,
        ICronEvaluator cronEvaluator,
        IQuietHoursReader quietHoursReader,
        IOptions<ReminderDigestOptions> digestOptions,
        IConfiguration configuration,
        ILogger<ReminderScanJobHandler> logger)
    {
        this.dbContext = dbContext;
        this.notificationService = notificationService;
        this.recipientResolvers = recipientResolvers;
        this.renderers = renderers;
        this.cronEvaluator = cronEvaluator;
        this.quietHoursReader = quietHoursReader;
        this.digestOptions = digestOptions.Value;
        this.logger = logger;
        timeZone = ResolveTimeZone(configuration, logger);
    }

    private static TimeZoneInfo ResolveTimeZone(IConfiguration configuration, ILogger logger)
    {
        try
        {
            return Helper.GetTimezone(configuration);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not resolve the deployment time zone; quiet hours will be interpreted in UTC.");
            return TimeZoneInfo.Utc;
        }
    }

    /// <summary>The handler key 03b registers the recurring scan job under.</summary>
    public const string HandlerKey = "reminders.scan";

    public string Key => HandlerKey;

    /// <summary>Outcome of attempting one occurrence — distinguishes a normal terminal row from a quiet-hours defer.</summary>
    private enum DispatchAction
    {
        /// <summary>A terminal dispatch row (Sent / Skipped / Failed) was written for the occurrence.</summary>
        Logged,

        /// <summary>Quiet hours: nothing sent, no row written, the schedule left untouched for the next scan to retry.</summary>
        DeferredQuietHours
    }

    /// <summary>Bounds one scan run so a backlog doesn't blow it up; the next run picks up the rest.</summary>
    private const int ScanBatchSize = 500;

    /// <summary>Guards the catch-up enumeration against a pathological schedule (never an expected path).</summary>
    private const int MaxOccurrencesPerRun = 10_000;

    public async Task ExecuteAsync(ScheduledJobContext context, CancellationToken ct)
    {
        // Drive "now" from the fire instant (not DateTimeOffset.UtcNow) so the scan is deterministic and
        // testable at an arbitrary instant; in production this is the actual fire time.
        var now = new DateTimeOffset(DateTime.SpecifyKind(context.ActualFireTimeUtc, DateTimeKind.Utc));
        var correlationId = string.IsNullOrEmpty(context.CorrelationId)
            ? Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N")
            : context.CorrelationId;

        // 1. Query due Active reminders. Bounded batch; reloaded per id below so an unexpected failure on one
        //    can ChangeTracker.Clear without detaching the rest of the batch. The DigestKey is read here so
        //    the batch can be split into the per-occurrence path (no key) and the digest path (grouped by key).
        var due = await dbContext.Set<ReminderDefinition>()
            .Where(d => d.Status == ReminderStatus.Active
                        && d.NextOccurrenceAt != null
                        && d.NextOccurrenceAt <= now)
            .OrderBy(d => d.NextOccurrenceAt)
            .Take(ScanBatchSize)
            .Select(d => new { d.Id, d.DigestKey })
            .ToListAsync(ct);

        // Non-digest definitions: one send per due occurrence (phase 03 / 04a) — unchanged.
        foreach (var id in due.Where(d => d.DigestKey == null).Select(d => d.Id))
            try
            {
                var def = await dbContext.Set<ReminderDefinition>()
                    .Include(d => d.Recipients)
                    .Include(d => d.LeadOffsets)
                    .FirstOrDefaultAsync(d => d.Id == id, ct);

                // Re-check the seek predicate: the reminder may have been paused / cancelled / advanced since
                // the id list was read.
                if (def is null || def.Status != ReminderStatus.Active
                                || def.NextOccurrenceAt is not { } next || next > now)
                    continue;

                await ProcessDefinitionAsync(def, now, correlationId, ct);
            }
            catch (Exception ex)
            {
                // Failure isolation backstop: a bad reminder must not abort the batch. Expected dispatch
                // failures are handled inline (logged as a Failed row); reaching here is unexpected — drop the
                // partial tracked changes so they can't poison a sibling's save, then continue.
                logger.LogError(ex, "Unexpected error scanning reminder {DefinitionId}; skipping.", id);
                dbContext.ChangeTracker.Clear();
            }

        // Digest definitions (phase 04b): aggregate per key — one notification per recipient across the key's
        // due definitions. Each key is isolated so one bad key can't abort the others.
        foreach (var group in due.Where(d => d.DigestKey != null).GroupBy(d => d.DigestKey!))
            try
            {
                await ProcessDigestKeyAsync(group.Key, group.Select(d => d.Id).ToList(), now, correlationId, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error scanning digest key '{DigestKey}'; skipping.", group.Key);
                dbContext.ChangeTracker.Clear();
            }

        // Second due-source (phase 05b): snoozed occurrences whose SnoozeUntil has passed. The definition scan
        // above can never revisit these — NextOccurrenceAt has already advanced past the original occurrence —
        // so they are re-delivered here, deduped by the marker's own state (not the occurrence-keyed dispatch
        // dedup, which the original occurrence's Sent row would wrongly trip).
        await ProcessDueSnoozesAsync(now, correlationId, ct);
    }

    private async Task ProcessDefinitionAsync(ReminderDefinition def, DateTimeOffset now, string correlationId, CancellationToken ct)
    {
        var dispatched = await LoadEffectiveOccurrencesAsync(def.Id, ct);

        // 2–3. Enumerate occurrences now due (<= now) that have no non-reversed dispatch row.
        var dueOccurrences = EnumerateDueOccurrences(def, now, dispatched);
        if (dueOccurrences.Count == 0)
        {
            // Nothing actually due (already dispatched / stale NextOccurrenceAt) — just advance past it.
            AdvanceSchedule(def, now, dispatched);
            await dbContext.SaveChangesAsync(ct);
            return;
        }

        // Catch-up: act on the most-recent due occurrence; collapse the older missed ones to Skipped rows.
        var target = dueOccurrences[^1]; // ascending → last is the most recent

        var action = await DispatchOccurrenceAsync(def, target, now, correlationId, ct);
        if (action == DispatchAction.DeferredQuietHours)
            // Quiet hours: send nothing, write no row, and leave NextOccurrenceAt pointing at the due occurrence.
            // (We deliberately do NOT push it to the window end — the shared ReminderOccurrenceCalculator would
            // then skip this overdue occurrence entirely. Instead the next scan re-evaluates and dispatches on
            // the first sweep after quiet hours end. Re-evaluation is cheap: a quiet-hours check, no send, no row.)
            // We also leave the older missed occurrences uncollapsed so they aren't lost across the defer.
            return;

        // The target produced a terminal row. Now collapse the older missed instants to Skipped rows for audit so
        // the next scan can't re-pick them, then advance past everything.
        CollapseOlderMissed(def, dueOccurrences.Take(dueOccurrences.Count - 1), now, correlationId, dispatched);
        dispatched.Add(target);

        // 8. Advance: recompute over the full dispatched set (incl. the just-acted instants), so the next run
        //    sees only future occurrences; no remaining occurrence ⇒ Completed.
        AdvanceSchedule(def, now, dispatched);
        await SaveDispatchAsync(ct);
    }

    /// <summary>
    /// Resolve recipients + text and dispatch one occurrence, appending exactly one terminal dispatch row — unless
    /// quiet hours defer it (<see cref="DispatchAction.DeferredQuietHours"/>: nothing sent, no row, schedule left
    /// for the next scan). The phase-04 dispatch policy lives here: the per-kind opt-out filters the recipients
    /// (all opted out ⇒ a <see cref="SkipReason.OptedOut"/> row) and quiet hours defer the dispatch. Any failure
    /// (missing strategy, resolver/renderer throw, <c>NotifyAsync</c> throw) is isolated to a
    /// <see cref="DispatchOutcome.Failed"/> row — never a thrown batch and never a silent skip.
    /// </summary>
    private async Task<DispatchAction> DispatchOccurrenceAsync(ReminderDefinition def, DateTimeOffset occurrence, DateTimeOffset now, string correlationId, CancellationToken ct)
    {
        // 4. Recipients + per-kind opt-out (phase 04a). A terminal resolution already wrote its own row
        //    (Failed / Skipped-NoRecipients / Skipped-OptedOut) — nothing left to send.
        var resolution = await ResolveRecipientsAsync(def, occurrence, now, correlationId, ct);
        if (resolution.Terminal)
            return DispatchAction.Logged;
        var recipientIds = resolution.Recipients;

        // 4c. Quiet hours (phase 04a) — defer (don't drop) the whole occurrence if EVERY remaining recipient
        //     is inside their quiet window at the dispatch instant. Evaluated at `now` (when the user would
        //     actually be disturbed), not the historical occurrence instant. With a per-occurrence ledger the
        //     dispatch can't be split per recipient, so v1 defers only when it would otherwise wake everyone;
        //     a mixed window (some quiet, some not) dispatches.
        if (await AllRecipientsInQuietHoursAsync(recipientIds, now, ct))
        {
            logger.LogDebug("Reminder {DefinitionId} occurrence {Occurrence:o} deferred: all recipients in quiet hours.",
                def.Id, occurrence);
            return DispatchAction.DeferredQuietHours;
        }

        try
        {
            // 5. Text/payload — fixed precedence: a set NotificationType wins (even if a renderer also exists
            //    for the template key); the keyed renderer is the fallback when NotificationType is null.
            NotificationType type;
            INotificationPayload? payload;
            if (def.NotificationType is { } notificationType)
            {
                type = notificationType;
                // Rehydration, not authoring: the document was written through the guarded
                // ReminderRegistration.Payload seam, so forwarding it verbatim keeps the payload PII
                // contract intact (see RawNotificationPayload).
                payload = RawNotificationPayload.Parse(def.PayloadJson);
            }
            else
            {
                var renderer = renderers.FirstOrDefault(r => r.TemplateKey == def.TemplateKey);
                if (renderer is null)
                {
                    logger.LogError("Reminder {DefinitionId} template '{TemplateKey}' has neither a NotificationType nor a registered renderer; dispatch failed.",
                        def.Id, def.TemplateKey);
                    AppendDispatch(def, occurrence, now, DispatchOutcome.Failed, correlationId,
                        null, null, recipientIds);
                    return DispatchAction.Logged;
                }

                var rendered = renderer.Render(new ReminderRenderContext(ToKey(def), def.TemplateKey, ParsePayload(def.PayloadJson), occurrence));
                type = rendered.Type;
                payload = rendered.Payload;
            }

            // 6. Dispatch through the Notification module — never touch transport/channels here.
            await notificationService.NotifyAsync(NotificationRecipients.Users(recipientIds), type, payload, ct);

            // 7. Log success + remember the dispatched instant.
            AppendDispatch(def, occurrence, now, DispatchOutcome.Sent, correlationId,
                null, type, recipientIds);
            def.LastOccurrenceAt = occurrence;
            return DispatchAction.Logged;
        }
        catch (Exception ex)
        {
            // One bad reminder must not abort the batch — log a Failed row and move on. The non-reversed row
            // means the next scan won't retry this occurrence (a re-dispatch would be a phase-05 reversal).
            logger.LogError(ex, "Dispatch failed for reminder {DefinitionId} occurrence {Occurrence:o}.", def.Id, occurrence);
            AppendDispatch(def, occurrence, now, DispatchOutcome.Failed, correlationId,
                null, def.NotificationType, null);
            return DispatchAction.Logged;
        }
    }

    /// <summary>Outcome of resolving + opt-out-filtering an occurrence's recipients.</summary>
    /// <param name="Recipients">The recipients to dispatch to — non-empty when <paramref name="Terminal"/> is false.</param>
    /// <param name="Terminal">When true, a terminal dispatch row (Failed / Skipped-NoRecipients / Skipped-OptedOut)
    /// was already appended; the caller sends nothing and just advances the schedule.</param>
    private readonly record struct RecipientResolution(IReadOnlyList<long> Recipients, bool Terminal)
    {
        public static readonly RecipientResolution TerminalRow = new([], true);

        public static RecipientResolution Resolved(IReadOnlyList<long> recipients) => new(recipients, false);
    }

    /// <summary>
    /// Resolve an occurrence's recipients (explicit list or the owner's keyed resolver) then apply the phase-04a
    /// per-kind opt-out. Shared by the per-occurrence path (<see cref="DispatchOccurrenceAsync"/>) and the digest
    /// path (<see cref="ProcessDigestKeyAsync"/>). On any terminal condition — resolver missing, resolver throw,
    /// no recipients, or all opted out — it appends the appropriate Failed / Skipped row itself and returns
    /// <see cref="RecipientResolution.TerminalRow"/>; otherwise it returns the (non-empty) surviving recipients.
    /// </summary>
    private async Task<RecipientResolution> ResolveRecipientsAsync(ReminderDefinition def, DateTimeOffset occurrence, DateTimeOffset now, string correlationId, CancellationToken ct)
    {
        try
        {
            IReadOnlyCollection<long> recipientIds;
            if (def.RecipientMode == RecipientMode.ExplicitUsers)
            {
                recipientIds = def.Recipients.Select(r => r.UserId).Distinct().ToList();
            }
            else
            {
                var resolver = recipientResolvers.FirstOrDefault(r => r.Key == def.RecipientResolverKey);
                if (resolver is null)
                {
                    // The owning module is absent at dispatch time (02a validated it existed at registration).
                    logger.LogError("No IReminderRecipientResolver for key '{ResolverKey}' (reminder {DefinitionId}); dispatch failed.",
                        def.RecipientResolverKey, def.Id);
                    AppendDispatch(def, occurrence, now, DispatchOutcome.Failed, correlationId,
                        null, def.NotificationType, null);
                    return RecipientResolution.TerminalRow;
                }

                recipientIds = await resolver.ResolveAsync(
                    new ReminderResolutionContext(ToKey(def), def.TemplateKey, ParsePayload(def.PayloadJson), occurrence), ct);
            }

            if (recipientIds.Count == 0)
            {
                AppendDispatch(def, occurrence, now, DispatchOutcome.Skipped, correlationId,
                    SkipReason.NoRecipients, def.NotificationType, null);
                return RecipientResolution.TerminalRow;
            }

            // Per-kind opt-out (phase 04a) — absence-means-enabled: only an explicit Enabled=false row for
            // (UserId, OwnerModule, Kind) suppresses a recipient. All opted out ⇒ a logged OptedOut skip.
            var optedOut = await LoadOptedOutUserIdsAsync(def.OwnerModule, def.Kind, recipientIds, ct);
            var survivors = optedOut.Count > 0
                ? recipientIds.Where(id => !optedOut.Contains(id)).ToList()
                : recipientIds.ToList();

            if (survivors.Count == 0)
            {
                AppendDispatch(def, occurrence, now, DispatchOutcome.Skipped, correlationId,
                    SkipReason.OptedOut, def.NotificationType, null);
                return RecipientResolution.TerminalRow;
            }

            // Per-recipient snooze/dismiss markers (phase 05b) — drop any recipient who dismissed or snoozed
            // *this* occurrence, logging a per-recipient Skipped row for the audit trail. A snoozed recipient's
            // re-delivery is owned by the separate "due snoozes" source (ProcessDueSnoozesAsync); a dismissed
            // recipient is suppressed for this occurrence entirely.
            var afterMarkers = await ApplyOccurrenceMarkersAsync(def, occurrence, now, correlationId, survivors, ct);
            if (afterMarkers.Count == 0)
                return RecipientResolution.TerminalRow; // everyone dismissed/snoozed — per-recipient rows already written

            return RecipientResolution.Resolved(afterMarkers);
        }
        catch (Exception ex)
        {
            // A resolver throw (or any unexpected resolution error) fails safe to a logged Failed row.
            logger.LogError(ex, "Recipient resolution failed for reminder {DefinitionId} occurrence {Occurrence:o}.", def.Id, occurrence);
            AppendDispatch(def, occurrence, now, DispatchOutcome.Failed, correlationId,
                null, def.NotificationType, null);
            return RecipientResolution.TerminalRow;
        }
    }

    /// <summary>
    /// The occurrence instants at/after <see cref="ReminderDefinition.NextOccurrenceAt"/> and at/before
    /// <paramref name="now"/> with no non-reversed dispatch row, ascending — walked via the shared calculator
    /// (the growing <paramref name="dispatched"/> skip set advances it past each collected occurrence).
    /// </summary>
    private List<DateTimeOffset> EnumerateDueOccurrences(ReminderDefinition def, DateTimeOffset now, IReadOnlySet<DateTimeOffset> dispatched)
    {
        var result = new List<DateTimeOffset>();
        var skip = new HashSet<DateTimeOffset>(dispatched);
        var from = def.NextOccurrenceAt ?? now;

        var i = 0;
        for (; i < MaxOccurrencesPerRun; i++)
        {
            var occ = ReminderOccurrenceCalculator.ComputeNextOccurrence(def, from, cronEvaluator, skip);
            if (occ is not { } value || value > now)
                break;
            result.Add(value);
            skip.Add(value); // exclude it so the next probe returns the following occurrence
        }

        // Hitting the cap means a high-frequency schedule is due more than MaxOccurrencesPerRun times in one
        // run (e.g. a per-minute cron after long downtime). We then truncate at the Nth-*oldest* occurrence, so
        // the catch-up target is stale rather than the most recent due instant. This is never an expected path
        // and self-heals over subsequent scans, but a stale burst goes out — log it so the schedule is visible.
        if (i == MaxOccurrencesPerRun)
            logger.LogWarning(
                "Reminder {DefinitionId} produced more than {Cap} due occurrences in one scan; " +
                "truncating at the oldest. Catch-up target is stale this run and will self-heal on the next scan.",
                def.Id, MaxOccurrencesPerRun);

        return result;
    }

    /// <summary>
    /// Recompute <see cref="ReminderDefinition.NextOccurrenceAt"/> from <paramref name="now"/> over the full
    /// dispatched set via the shared calculator; no remaining occurrence ⇒ <see cref="ReminderStatus.Completed"/>.
    /// </summary>
    private void AdvanceSchedule(ReminderDefinition def, DateTimeOffset now, IReadOnlySet<DateTimeOffset> dispatched)
    {
        var next = ReminderOccurrenceCalculator.ComputeNextOccurrence(def, now, cronEvaluator, dispatched);
        if (next is null)
        {
            def.Status = ReminderStatus.Completed;
            def.CompletedAt = now;
            def.NextOccurrenceAt = null;
        }
        else
        {
            def.NextOccurrenceAt = next;
        }
    }

    /// <summary>
    /// Collapse a definition's older missed occurrences (everything but the catch-up target) to
    /// <see cref="DispatchOutcome.Skipped"/> / <see cref="SkipReason.Other"/> rows for audit, adding each to
    /// <paramref name="dispatched"/> so the schedule advance skips past them. Shared by the per-occurrence and
    /// digest paths (one send per reminder per run — no stale storm).
    /// </summary>
    private void CollapseOlderMissed(ReminderDefinition def, IEnumerable<DateTimeOffset> older, DateTimeOffset now, string correlationId, HashSet<DateTimeOffset> dispatched)
    {
        foreach (var missed in older)
        {
            AppendDispatch(def, missed, now, DispatchOutcome.Skipped, correlationId,
                SkipReason.Other, def.NotificationType, null);
            dispatched.Add(missed);
        }
    }

    // ---- phase 04b: digest batching ----

    /// <summary>One digest-eligible definition's contribution to a key: its catch-up target + the recipients it goes to.</summary>
    private sealed record DigestCandidate(
        ReminderDefinition Definition,
        DateTimeOffset Target,
        IReadOnlyList<DateTimeOffset> OlderMissed,
        IReadOnlyList<long> Recipients,
        HashSet<DateTimeOffset> Dispatched);

    /// <summary>
    /// Process one <see cref="ReminderDefinition.DigestKey"/> group (phase 04b): aggregate the key's due
    /// definitions so each recipient gets <b>one</b> <see cref="NotificationType.ReminderDigest"/> notification
    /// across every definition they're a recipient of, while still writing <b>one per-occurrence dispatch row
    /// per definition</b> (the audit trail is never collapsed — snooze/dismiss in phase 05 stays per occurrence).
    /// <para>
    /// Each definition contributes its catch-up <i>target</i> (the most-recent due occurrence; older missed
    /// instants collapse to skip rows exactly as the per-occurrence path). The whole key shares one flush
    /// decision — the window is a property of the key (<see cref="ReminderDigestOptions"/>), driven by the
    /// earliest due target across the key's definitions. Not yet elapsed ⇒ every candidate is left untouched
    /// (no rows, no advance) and the next scan reconsiders it; the row-present⇒skip dedup keeps that safe.
    /// </para>
    /// </summary>
    private async Task ProcessDigestKeyAsync(string digestKey, IReadOnlyList<long> ids, DateTimeOffset now, string correlationId, CancellationToken ct)
    {
        var window = digestOptions.Resolve(digestKey);
        var candidates = new List<DigestCandidate>();

        foreach (var id in ids)
        {
            var def = await dbContext.Set<ReminderDefinition>()
                .Include(d => d.Recipients)
                .Include(d => d.LeadOffsets)
                .FirstOrDefaultAsync(d => d.Id == id, ct);

            // Re-check the seek predicate (may have been paused / cancelled / advanced since the id list).
            if (def is null || def.Status != ReminderStatus.Active
                            || def.NextOccurrenceAt is not { } next || next > now)
                continue;

            var dispatched = await LoadEffectiveOccurrencesAsync(def.Id, ct);
            var dueOccurrences = EnumerateDueOccurrences(def, now, dispatched);
            if (dueOccurrences.Count == 0)
            {
                // Nothing actually due (already dispatched / stale NextOccurrenceAt) — just advance past it.
                AdvanceSchedule(def, now, dispatched);
                await dbContext.SaveChangesAsync(ct);
                continue;
            }

            var target = dueOccurrences[^1];
            var older = dueOccurrences.Take(dueOccurrences.Count - 1).ToList();

            var resolution = await ResolveRecipientsAsync(def, target, now, correlationId, ct);
            if (resolution.Terminal)
            {
                // No recipients / failed: a terminal row for the target was already written. This definition
                // never enters the digest — collapse the older instants and advance now, independent of the
                // key's flush window (there is nothing to batch).
                CollapseOlderMissed(def, older, now, correlationId, dispatched);
                dispatched.Add(target);
                AdvanceSchedule(def, now, dispatched);
                await dbContext.SaveChangesAsync(ct);
                continue;
            }

            candidates.Add(new DigestCandidate(def, target, older, resolution.Recipients, dispatched));
        }

        if (candidates.Count == 0)
            return;

        // Flush gate — the window is per key, so the whole key flushes together on the earliest due target.
        var earliest = candidates.Min(c => c.Target);
        if (!ReminderDigestPolicy.ShouldFlush(window, earliest, now, timeZone))
            return; // hold: leave every candidate untouched; the next scan reconsiders.

        // Quiet hours (defer, don't drop): if EVERY recipient across the batch is inside their window at `now`,
        // defer the whole flush — nothing sent, no rows — so a quiet-hours occurrence is never prematurely
        // digested. A mixed window dispatches to all (mirrors the per-occurrence 04a rule).
        var allRecipients = candidates.SelectMany(c => c.Recipients).Distinct().ToList();
        if (await AllRecipientsInQuietHoursAsync(allRecipients, now, ct))
        {
            logger.LogDebug("Digest key '{DigestKey}' deferred: all {Count} recipient(s) in quiet hours.", digestKey, allRecipients.Count);
            return;
        }

        // Flush: one NotifyAsync per recipient, then one per-occurrence row per definition (all sharing the
        // digest correlation id) plus the collapsed older rows, then advance every definition.
        var outcome = await SendDigestsAsync(candidates, correlationId, ct);

        foreach (var c in candidates)
        {
            AppendDispatch(c.Definition, c.Target, now, outcome, correlationId,
                null, NotificationType.ReminderDigest, c.Recipients);
            CollapseOlderMissed(c.Definition, c.OlderMissed, now, correlationId, c.Dispatched);
            if (outcome == DispatchOutcome.Sent)
                c.Definition.LastOccurrenceAt = c.Target;
            c.Dispatched.Add(c.Target);
            AdvanceSchedule(c.Definition, now, c.Dispatched);
        }

        await SaveDispatchAsync(ct);
    }

    /// <summary>
    /// Send the aggregated digest: group the candidates' targets by recipient and fire <b>one</b>
    /// <see cref="NotificationType.ReminderDigest"/> <c>NotifyAsync</c> per recipient (a count-by-kind
    /// payload). Each send is isolated; returns <see cref="DispatchOutcome.Sent"/>
    /// if at least one recipient send succeeded, else <see cref="DispatchOutcome.Failed"/> (the schedule still
    /// advances — a re-dispatch would be a phase-05 reversal).
    /// </summary>
    private async Task<DispatchOutcome> SendDigestsAsync(IReadOnlyList<DigestCandidate> candidates, string correlationId, CancellationToken ct)
    {
        var byRecipient = new Dictionary<long, List<DigestCandidate>>();
        foreach (var candidate in candidates)
        foreach (var userId in candidate.Recipients)
        {
            if (!byRecipient.TryGetValue(userId, out var list))
                byRecipient[userId] = list = [];
            list.Add(candidate);
        }

        var anyAttempted = false;
        var anySucceeded = false;
        foreach (var (userId, items) in byRecipient)
        {
            anyAttempted = true;
            try
            {
                await notificationService.NotifyAsync(
                    NotificationRecipients.Users([userId]), BuildDigestPayload(items), ct);
                anySucceeded = true;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Digest NotifyAsync failed for recipient {UserId} ({Count} reminder(s)).", userId, items.Count);
            }
        }

        return !anyAttempted || anySucceeded ? DispatchOutcome.Sent : DispatchOutcome.Failed;
    }

    /// <summary>
    /// The digest payload for one recipient — a total count plus a count-by-<see cref="ReminderDefinition.Kind"/>
    /// breakdown (the Notification renderer reads <c>count</c>). Kinds are non-PII category strings, never a
    /// person's data.
    /// </summary>
    private static ReminderDigestPayload BuildDigestPayload(IReadOnlyList<DigestCandidate> items)
    {
        return new ReminderDigestPayload(
            items.Count,
            items
                .GroupBy(i => i.Definition.Kind)
                .Select(g => new ReminderDigestKindCount(g.Key.ToString(), g.Count()))
                .OrderByDescending(k => k.Count)
                .ToList());
    }

    // ---- phase 05b: per-recipient snooze / dismiss ----

    /// <summary>
    /// Drop recipients who dismissed or snoozed <i>this</i> occurrence (their effective, non-reversed
    /// <see cref="ReminderOccurrenceAction"/>), appending a per-recipient <see cref="DispatchOutcome.Skipped"/>
    /// row (<see cref="SkipReason.Dismissed"/> / <see cref="SkipReason.Snoozed"/>) for each so the audit trail
    /// records the suppression. The shared <c>NextOccurrenceAt</c> is untouched — only the per-recipient send is
    /// affected — and a snoozed recipient is re-delivered later by <see cref="ProcessDueSnoozesAsync"/>.
    /// </summary>
    private async Task<IReadOnlyList<long>> ApplyOccurrenceMarkersAsync(
        ReminderDefinition def, DateTimeOffset occurrence, DateTimeOffset now, string correlationId,
        IReadOnlyList<long> recipientIds, CancellationToken ct)
    {
        var effective = await LoadEffectiveActionsAsync(def.Id, occurrence, recipientIds, ct);
        if (effective.Count == 0)
            return recipientIds;

        var survivors = new List<long>(recipientIds.Count);
        foreach (var id in recipientIds)
            if (effective.TryGetValue(id, out var action))
            {
                var reason = action == ReminderActionType.Dismiss ? SkipReason.Dismissed : SkipReason.Snoozed;
                AppendDispatch(def, occurrence, now, DispatchOutcome.Skipped, correlationId,
                    reason, def.NotificationType, [id]);
            }
            else
            {
                survivors.Add(id);
            }

        return survivors;
    }

    /// <summary>
    /// The <b>latest non-reversed</b> snooze/dismiss action per recipient for a single
    /// <c>(definition, occurrence)</c>, over the given candidate user ids. An action reversed by a later
    /// correction row (<see cref="ReminderOccurrenceAction.ReversesActionId"/>) is excluded; if a recipient has
    /// both a snooze and a later dismiss (or vice-versa) the most recent <see cref="ReminderOccurrenceAction.ActedAt"/>
    /// wins.
    /// </summary>
    private async Task<Dictionary<long, ReminderActionType>> LoadEffectiveActionsAsync(
        long definitionId, DateTimeOffset occurrence, IReadOnlyCollection<long> userIds, CancellationToken ct)
    {
        var reversedIds = dbContext.Set<ReminderOccurrenceAction>()
            .Where(a => a.ReminderDefinitionId == definitionId && a.ReversesActionId != null)
            .Select(a => a.ReversesActionId!.Value);

        var actions = await dbContext.Set<ReminderOccurrenceAction>()
            .Where(a => a.ReminderDefinitionId == definitionId
                        && a.OccurrenceAt == occurrence
                        && a.ActionType != ReminderActionType.Reversal
                        && !reversedIds.Contains(a.Id)
                        && userIds.Contains(a.UserId))
            .Select(a => new { a.UserId, a.ActionType, a.ActedAt })
            .ToListAsync(ct);

        return actions
            .GroupBy(a => a.UserId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.ActedAt).First().ActionType);
    }

    /// <summary>
    /// The second due-source: snooze markers whose <see cref="ReminderOccurrenceAction.SnoozeUntil"/> has
    /// passed, are not reversed, are not superseded by a later action for the same key, and have <b>not been
    /// delivered</b> (no <see cref="ReminderDispatch"/> references them). Each is dispatched as a
    /// <b>single-recipient</b> send for its <c>(definition, occurrence)</c> — reusing 03a's text-resolution +
    /// log steps — and writes a recipient-scoped, marker-linked dispatch row that resolves the marker so it
    /// never re-fires. The original occurrence's existing <c>Sent</c> row is irrelevant here: dedup is
    /// marker-state, not occurrence-keyed.
    /// </summary>
    private async Task ProcessDueSnoozesAsync(DateTimeOffset now, string correlationId, CancellationToken ct)
    {
        var reversedIds = dbContext.Set<ReminderOccurrenceAction>()
            .Where(a => a.ReversesActionId != null)
            .Select(a => a.ReversesActionId!.Value);

        var dueSnoozes = await dbContext.Set<ReminderOccurrenceAction>()
            .Where(a => a.ActionType == ReminderActionType.Snooze
                        && a.SnoozeUntil != null
                        && a.SnoozeUntil <= now
                        && !reversedIds.Contains(a.Id)
                        // Not delivered: no dispatch references this snooze marker (marker-state dedup).
                        && !dbContext.Set<ReminderDispatch>().Any(d => d.ReminderOccurrenceActionId == a.Id)
                        // Not superseded by a later action (re-snooze / dismiss / reversal) for the same key.
                        && !dbContext.Set<ReminderOccurrenceAction>().Any(later =>
                            later.ReminderDefinitionId == a.ReminderDefinitionId
                            && later.OccurrenceAt == a.OccurrenceAt
                            && later.UserId == a.UserId
                            && later.Id != a.Id
                            && later.ActedAt > a.ActedAt))
            .OrderBy(a => a.SnoozeUntil)
            .Take(ScanBatchSize)
            .ToListAsync(ct);

        foreach (var marker in dueSnoozes)
            try
            {
                await DispatchSnoozedOccurrenceAsync(marker, now, correlationId, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error dispatching snoozed occurrence (action {ActionId}); skipping.", marker.Id);
                dbContext.ChangeTracker.Clear();
            }
    }

    /// <summary>
    /// Dispatch one due snooze marker to its single recipient — same text precedence as the per-occurrence path
    /// (a set <c>NotificationType</c> wins, the keyed renderer is the fallback). Always writes a marker-linked
    /// <see cref="ReminderDispatch"/> row (Sent on success, Failed on a missing text source / send throw) so the
    /// marker is resolved either way (a non-reversed Failed row means no retry — a re-dispatch would be a
    /// reversal). The definition's <c>NextOccurrenceAt</c> is never touched.
    /// <para>
    /// <b>Dispatch policy on the re-delivery path (phase 04a interaction):</b> the per-kind opt-out
    /// (<see cref="ReminderKindPreference"/>) still applies — a recipient who muted this kind after snoozing has
    /// the re-delivery dropped to a marker-linked <see cref="SkipReason.OptedOut"/> row (the snooze still resolves
    /// and never re-fires; "mute this whole kind" outranks a stale snooze). Quiet hours, by contrast, do <b>not</b>
    /// re-defer a due snooze: the user explicitly chose the <c>SnoozeUntil</c> instant, so honouring it wins over
    /// the quiet window (unlike the per-occurrence path, where no instant was chosen by the user).
    /// </para>
    /// </summary>
    private async Task DispatchSnoozedOccurrenceAsync(ReminderOccurrenceAction marker, DateTimeOffset now, string correlationId, CancellationToken ct)
    {
        var def = await dbContext.Set<ReminderDefinition>()
            .FirstOrDefaultAsync(d => d.Id == marker.ReminderDefinitionId, ct);
        if (def is null)
            return; // the definition is gone — nothing to render/send

        // Per-kind opt-out (phase 04a) — a recipient who muted this kind (even after snoozing) does not get the
        // re-delivery. Resolve the marker with an OptedOut row so it never re-fires (mirrors the per-occurrence
        // ResolveRecipientsAsync all-opted-out branch).
        var optedOut = await LoadOptedOutUserIdsAsync(def.OwnerModule, def.Kind, [marker.UserId], ct);
        if (optedOut.Count > 0)
        {
            AppendSnoozeDispatch(def, marker, now, DispatchOutcome.Skipped, correlationId, def.NotificationType,
                SkipReason.OptedOut, false);
            await dbContext.SaveChangesAsync(ct);
            return;
        }

        try
        {
            NotificationType type;
            INotificationPayload? payload;
            if (def.NotificationType is { } notificationType)
            {
                type = notificationType;
                // Rehydration, not authoring: the document was written through the guarded
                // ReminderRegistration.Payload seam, so forwarding it verbatim keeps the payload PII
                // contract intact (see RawNotificationPayload).
                payload = RawNotificationPayload.Parse(def.PayloadJson);
            }
            else
            {
                var renderer = renderers.FirstOrDefault(r => r.TemplateKey == def.TemplateKey);
                if (renderer is null)
                {
                    logger.LogError("Snoozed reminder {DefinitionId} occurrence {Occurrence:o} template '{TemplateKey}' has neither a NotificationType nor a registered renderer; dispatch failed.",
                        def.Id, marker.OccurrenceAt, def.TemplateKey);
                    AppendSnoozeDispatch(def, marker, now, DispatchOutcome.Failed, correlationId, null,
                        null, false);
                    await dbContext.SaveChangesAsync(ct);
                    return;
                }

                var rendered = renderer.Render(new ReminderRenderContext(ToKey(def), def.TemplateKey, ParsePayload(def.PayloadJson), marker.OccurrenceAt));
                type = rendered.Type;
                payload = rendered.Payload;
            }

            await notificationService.NotifyAsync(NotificationRecipients.Users([marker.UserId]), type, payload, ct);
            AppendSnoozeDispatch(def, marker, now, DispatchOutcome.Sent, correlationId, type,
                null, true);
            await dbContext.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Snoozed dispatch failed for reminder {DefinitionId} occurrence {Occurrence:o}.", def.Id, marker.OccurrenceAt);
            AppendSnoozeDispatch(def, marker, now, DispatchOutcome.Failed, correlationId, def.NotificationType,
                null, false);
            await dbContext.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// Append a marker-linked (<see cref="ReminderDispatch.ReminderOccurrenceActionId"/>) snooze re-delivery row.
    /// <paramref name="sent"/> drives the recipient snapshot (only a real send records the recipient id); a
    /// Skipped / Failed row carries an empty snapshot exactly like the per-occurrence <see cref="AppendDispatch"/>.
    /// </summary>
    private void AppendSnoozeDispatch(ReminderDefinition def, ReminderOccurrenceAction marker, DateTimeOffset now, DispatchOutcome outcome, string correlationId, NotificationType? type,
        SkipReason? skipReason, bool sent)
    {
        dbContext.Add(new ReminderDispatch
        {
            ReminderDefinitionId = def.Id,
            OccurrenceAt = marker.OccurrenceAt,
            DispatchedAt = now,
            Outcome = outcome,
            SkipReason = skipReason,
            NotificationTypeSnapshot = type,
            TemplateKeySnapshot = def.TemplateKey,
            RecipientsSnapshot = SerializeRecipientIds(sent ? [marker.UserId] : null),
            CorrelationId = correlationId,
            ReminderOccurrenceActionId = marker.Id
        });
    }

    /// <summary>
    /// The occurrence instants of every <b>effective</b> dispatch row for the definition — a non-reversed row
    /// (its <see cref="DispatchOutcome"/> isn't <see cref="DispatchOutcome.Reversed"/> and it is not itself
    /// reversed by a later correction row). This is the dedup coordinate set: an occurrence present here is
    /// skipped, so a re-run / misfire / overlap never double-sends. (Phase 03 writes no reversals; the reversal
    /// exclusion keeps the check correct once phase 05's snooze/dismiss adds them.)
    /// </summary>
    private async Task<HashSet<DateTimeOffset>> LoadEffectiveOccurrencesAsync(long definitionId, CancellationToken ct)
    {
        var reversedIds = dbContext.Set<ReminderDispatch>()
            .Where(d => d.ReminderDefinitionId == definitionId && d.ReversesDispatchId != null)
            .Select(d => d.ReversesDispatchId!.Value);

        var occurrences = await dbContext.Set<ReminderDispatch>()
            .Where(d => d.ReminderDefinitionId == definitionId
                        && d.Outcome != DispatchOutcome.Reversed
                        && !reversedIds.Contains(d.Id))
            .Select(d => d.OccurrenceAt)
            .ToListAsync(ct);

        return occurrences.ToHashSet();
    }

    /// <summary>
    /// The subset of <paramref name="candidateIds"/> that have an explicit <c>Enabled = false</c>
    /// <see cref="ReminderKindPreference"/> for this <c>(OwnerModule, Kind)</c> — i.e. recipients who muted this
    /// reminder kind. Absence-means-enabled: a missing row (or an <c>Enabled = true</c> row) does not suppress.
    /// </summary>
    private async Task<HashSet<long>> LoadOptedOutUserIdsAsync(string ownerModule, string kind, IReadOnlyCollection<long> candidateIds, CancellationToken ct)
    {
        var ids = await dbContext.Set<ReminderKindPreference>()
            .Where(p => p.OwnerModule == ownerModule
                        && p.Kind == kind
                        && !p.Enabled
                        && candidateIds.Contains(p.UserId))
            .Select(p => p.UserId)
            .ToListAsync(ct);

        return ids.ToHashSet();
    }

    /// <summary>
    /// True only when <b>every</b> recipient has a quiet-hours window and the dispatch instant <paramref name="now"/>
    /// falls inside all of them (so dispatching would wake everyone). A recipient with no window is, by
    /// absence-means-enabled, never "in quiet hours", which makes this false ⇒ the occurrence dispatches.
    /// </summary>
    private async Task<bool> AllRecipientsInQuietHoursAsync(IReadOnlyCollection<long> recipientIds, DateTimeOffset now, CancellationToken ct)
    {
        var distinctIds = recipientIds.Distinct().ToList();

        // No recipients → there is no one whose quiet hours could defer the send. Guard against the
        // vacuous `windows.All(...) == true` over an empty set, which would otherwise defer forever.
        if (distinctIds.Count == 0)
            return false;

        // The window lives in the Notifications module now (follow-up 05); this seam is the only way in.
        var windows = await quietHoursReader.GetWindowsAsync(distinctIds, ct);

        // At least one recipient has no quiet-hours window → not "all in quiet hours".
        if (windows.Count < distinctIds.Count)
            return false;

        return windows.Values.All(w => QuietHoursPolicy.IsWithin(w, now, timeZone));
    }

    /// <summary>
    /// Persist a dispatched occurrence's pending changes, surviving a scan-vs-registry concurrency conflict.
    /// <para>
    /// The scan loaded the definition at one <c>row_version</c>; a concurrent admin registry action
    /// (cancel / pause / resume / re-register on the request thread) can bump it before this save — only
    /// scan-vs-scan is serialised (<c>DisallowConcurrent</c>), not scan-vs-registry. EF then throws
    /// <see cref="DbUpdateConcurrencyException"/> on the definition's <c>Modified</c> entry. By this point
    /// <c>NotifyAsync</c> has already fired, so the just-appended append-only <see cref="ReminderDispatch"/>
    /// ledger row(s) MUST commit — otherwise the next scan re-picks the occurrence and re-sends, because the
    /// ledger write that would have deduped it was the casualty. The admin action wins the definition itself,
    /// so we detach the conflicting (definition) entries — discarding our now-stale schedule advance — and
    /// re-save the remaining ledger inserts. Pure inserts carry no concurrency token, so once every conflicting
    /// definition is detached the retry commits cleanly. Non-conflicting definition updates (e.g. sibling
    /// definitions in a digest flush) are left tracked and committed by the retry.
    /// </para>
    /// <para>
    /// The detach+retry runs in a loop: a single digest flush can touch several definitions, and more than one
    /// may have been concurrently mutated, so each retry can surface a *fresh* conflict on a different row. We
    /// detach the conflicting entries and retry until the save succeeds with no conflict (bounded by
    /// <see cref="MaxConcurrencyRetries"/> as a safety net — in practice it converges within the number of
    /// definitions in the batch, since every iteration permanently removes at least one conflicting row).
    /// </para>
    /// </summary>
    private async Task SaveDispatchAsync(CancellationToken ct)
    {
        var detached = false;
        for (var attempt = 0;; attempt++)
            try
            {
                await dbContext.SaveChangesAsync(ct);
                if (detached)
                    logger.LogWarning(
                        "Reminder scan save hit a concurrency conflict (concurrent admin registry change); the dispatch ledger was preserved and the stale definition update(s) discarded.");
                return;
            }
            catch (DbUpdateConcurrencyException ex) when (attempt < MaxConcurrencyRetries)
            {
                foreach (var entry in ex.Entries)
                    entry.State = EntityState.Detached;
                detached = true;
            }
    }

    /// <summary>
    /// Safety bound on <see cref="SaveDispatchAsync"/>'s detach+retry loop. A run converges within the number of
    /// definitions in the batch (each iteration detaches ≥1 conflicting row); this only caps a pathological case
    /// so a conflict storm can't spin forever — exceeding it rethrows the final conflict to the per-group
    /// failure backstop in <see cref="ExecuteAsync"/>.
    /// </summary>
    private const int MaxConcurrencyRetries = ScanBatchSize + 1;

    private void AppendDispatch(
        ReminderDefinition def, DateTimeOffset occurrence, DateTimeOffset now, DispatchOutcome outcome,
        string correlationId, SkipReason? skipReason, NotificationType? type, IReadOnlyCollection<long>? recipientIds)
    {
        dbContext.Add(new ReminderDispatch
        {
            ReminderDefinitionId = def.Id,
            OccurrenceAt = occurrence,
            DispatchedAt = now,
            Outcome = outcome,
            SkipReason = skipReason,
            NotificationTypeSnapshot = type,
            TemplateKeySnapshot = def.TemplateKey,
            RecipientsSnapshot = SerializeRecipientIds(recipientIds),
            CorrelationId = correlationId
        });
    }

    /// <summary>jsonb snapshot of who it went to — user ids only, never PII (see <see cref="ReminderDispatch"/>).</summary>
    private static string SerializeRecipientIds(IReadOnlyCollection<long>? ids) => ids is null || ids.Count == 0 ? "[]" : JsonHelper.Serialize(ids);

    private static ReminderKey ToKey(ReminderDefinition def) => new(def.OwnerModule, def.SubjectType, def.SubjectId, def.Kind);

    /// <summary>
    /// The stored payload as the object a keyed <see cref="IReminderRenderer"/> reads (the dispatch path
    /// rehydrates through <see cref="RawNotificationPayload.Parse"/> instead). A <see cref="JsonNode"/>
    /// re-serialises to the exact stored JSON (its property names are preserved verbatim), so the renderer
    /// sees the same payload the owner registered. Only an absent/blank payload passes through as null — a
    /// registered empty object (<c>{}</c>) reaches the renderer as an empty node, so the two stay distinguishable.
    /// </summary>
    private static JsonNode? ParsePayload(string payloadJson) => string.IsNullOrWhiteSpace(payloadJson) ? null : JsonNode.Parse(payloadJson);
}