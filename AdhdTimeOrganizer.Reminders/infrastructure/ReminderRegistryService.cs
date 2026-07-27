using AdhdTimeOrganizer.Reminders.domain.entity;
using AdhdTimeOrganizer.Reminders.domain.@enum;
using AdhdTimeOrganizer.Reminders.domain.service;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MojaDigitalnaFirma.Kernel.reminders;
using MojaDigitalnaFirma.Kernel.scheduling;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.domain.helper;
using Sydowwe.Framework.domain.result;
using Sydowwe.Framework.infrastructure.persistence;

namespace AdhdTimeOrganizer.Reminders.infrastructure;

/// <summary>
/// The Core.Reminders implementation of <see cref="IReminderRegistry"/>: an idempotent upsert keyed by the
/// <see cref="ReminderKey"/> 4-tuple, plus cancel / pause / resume, all driven by the shared
/// <see cref="ReminderOccurrenceCalculator"/> so register/resume and the phase-03 scanner agree on the next
/// occurrence.
/// <para>
/// Auto-registered via the <see cref="IScopedService"/> marker (like NotificationService / SchedulerService)
/// and <b>safe to call with no authenticated user</b> — an owning module may register from a background scan
/// job. <see cref="ReminderDefinition"/> derives from the plain table base (not the user-scoped one), so the
/// background insert never hits the <c>UserId == 0</c> FK trap.
/// </para>
/// <para>
/// <b>Owner discipline (the contract expectation):</b> owners re-register on a date change (same key, new
/// schedule — upserts in place) and cancel on entity deletion. This module never reaches into the owner to
/// detect either; it only reacts to these declarative calls.
/// </para>
/// </summary>
public class ReminderRegistryService(
    DbContext dbContext,
    IEnumerable<IReminderRecipientResolver> recipientResolvers,
    IEnumerable<IReminderRenderer> renderers,
    ICronEvaluator cronEvaluator,
    ILogger<ReminderRegistryService> logger) : IReminderRegistry, IScopedService
{
    public async Task<ReminderRegistrationResult> RegisterAsync(ReminderRegistration registration, CancellationToken ct)
    {
        Validate(registration);

        var key = registration.Key;
        var existing = await dbContext.Set<ReminderDefinition>()
            .Include(d => d.Recipients)
            .Include(d => d.LeadOffsets)
            .FirstOrDefaultAsync(d =>
                d.OwnerModule == key.OwnerModule && d.SubjectType == key.SubjectType &&
                d.SubjectId == key.SubjectId && d.Kind == key.Kind, ct);

        var created = existing is null;
        var def = existing ?? new ReminderDefinition
        {
            OwnerModule = key.OwnerModule,
            SubjectType = key.SubjectType,
            SubjectId = key.SubjectId,
            Kind = key.Kind,
            TemplateKey = registration.TemplateKey,
            Status = ReminderStatus.Active
        };

        ApplyContentAndSchedule(def, registration);
        ApplyRecipients(def, registration);

        // Reactivation: re-registering a withdrawn reminder brings it back. A Cancelled/Completed row goes
        // Active again; a Paused row stays Paused (preserve the admin action — resume reactivates it).
        if (!created && def.Status is ReminderStatus.Cancelled or ReminderStatus.Completed)
        {
            def.Status = ReminderStatus.Active;
            def.CompletedAt = null;
        }

        def.NextOccurrenceAt = def.Status == ReminderStatus.Paused
            ? null
            : await ComputeNextOccurrenceAsync(def, ct);

        // Route the save through the Result-returning helpers (DbUtils.HandleException wrapping) per the
        // CLAUDE.md convention. The contract surface is ReminderRegistrationResult, not Result, so a DB failure
        // is re-thrown (as it was with the raw SaveChangesAsync) rather than returned — see EnsureSaved.
        EnsureSaved(created
            ? await dbContext.AddEntityAsync(def, ct)
            : await dbContext.UpdateEntityAsync(def, ct));

        logger.LogInformation(
            "Registered reminder {OwnerModule}/{SubjectType}/{Kind} ({Outcome}); next occurrence {NextOccurrenceAt:o}",
            key.OwnerModule, key.SubjectType, key.Kind, created ? "created" : "updated", def.NextOccurrenceAt);

        return new ReminderRegistrationResult(def.Id, key, created);
    }

    public async Task CancelAsync(ReminderKey key, CancellationToken ct)
    {
        var def = await FindByKeyAsync(key, ct);
        if (def is null)
            return; // idempotent no-op — owners cancel on deletion without knowing prior state

        def.Status = ReminderStatus.Cancelled;
        def.NextOccurrenceAt = null;
        EnsureSaved(await dbContext.UpdateEntityAsync(def, ct));
    }

    public async Task PauseAsync(ReminderKey key, CancellationToken ct)
    {
        var def = await FindByKeyAsync(key, ct);
        if (def is null || def.Status != ReminderStatus.Active)
            return; // idempotent — only an active reminder pauses

        def.Status = ReminderStatus.Paused;
        def.NextOccurrenceAt = null;
        EnsureSaved(await dbContext.UpdateEntityAsync(def, ct));
    }

    public async Task ResumeAsync(ReminderKey key, CancellationToken ct)
    {
        var def = await FindByKeyAsync(key, ct);
        if (def is null || def.Status != ReminderStatus.Paused)
            return; // idempotent — only a paused reminder resumes

        def.Status = ReminderStatus.Active;
        def.NextOccurrenceAt = await ComputeNextOccurrenceAsync(def, ct);
        EnsureSaved(await dbContext.UpdateEntityAsync(def, ct));
    }

    /// <summary>
    /// Re-throw a failed <see cref="DbContextHelper"/> save as an exception. The registry's contract methods
    /// return their own result shapes (<see cref="ReminderRegistrationResult"/> / void), not <see cref="Result"/>,
    /// so a DB failure can't be returned to the caller — this preserves the original raw-<c>SaveChangesAsync</c>
    /// throwing behaviour while still routing the save through <c>DbUtils.HandleException</c>.
    /// </summary>
    private static void EnsureSaved(Result result)
    {
        if (result.Failed)
            throw new InvalidOperationException(
                $"Reminder registry save failed ({result.ErrorType}): {result.ErrorMessage}");
    }

    private Task<ReminderDefinition?> FindByKeyAsync(ReminderKey key, CancellationToken ct)
    {
        return dbContext.Set<ReminderDefinition>()
            .Include(d => d.LeadOffsets)
            .FirstOrDefaultAsync(d =>
                d.OwnerModule == key.OwnerModule && d.SubjectType == key.SubjectType &&
                d.SubjectId == key.SubjectId && d.Kind == key.Kind, ct);
    }

    /// <summary>
    /// The soonest occurrence at/after now not already in the dispatch log — via the shared calculator so the
    /// scanner (phase 03) advances recurring reminders identically. A not-yet-saved definition (Id 0) has no
    /// dispatch rows, so the log lookup is skipped.
    /// </summary>
    private async Task<DateTimeOffset?> ComputeNextOccurrenceAsync(ReminderDefinition def, CancellationToken ct)
    {
        IReadOnlySet<DateTimeOffset>? dispatched = null;
        if (def.Id != 0)
        {
            var occurrences = await dbContext.Set<ReminderDispatch>()
                .Where(x => x.ReminderDefinitionId == def.Id)
                .Select(x => x.OccurrenceAt)
                .ToListAsync(ct);
            dispatched = occurrences.ToHashSet();
        }

        return ReminderOccurrenceCalculator.ComputeNextOccurrence(def, DateTimeOffset.UtcNow, cronEvaluator, dispatched);
    }

    /// <summary>
    /// Serializes a registration payload to the stored camelCase document.
    /// <para>
    /// Two things are load-bearing here. (1) A <see cref="RawReminderPayload"/> is already a JSON document (an
    /// admin posted it) — write it out verbatim, never wrapped in a <c>{"json": …}</c> envelope. (2) Everything
    /// else is serialized <b>as <c>object</c></b> on purpose: a generic <c>Serialize&lt;T&gt;</c> would infer
    /// <see cref="IReminderPayload"/>, the declared type, and emit <c>{}</c> because the marker has no members.
    /// <c>JsonHelper</c> (not raw <c>JsonSerializer</c>) because the stored keys must be camelCase — the keyed
    /// renderers read them by name.
    /// </para>
    /// </summary>
    private static string SerializePayload(IReminderPayload? payload)
    {
        return payload switch
        {
            null => "{}",
            RawReminderPayload raw => raw.Json?.ToJsonString() ?? "{}",
            _ => JsonHelper.Serialize<object>(payload)
        };
    }

    private static void ApplyContentAndSchedule(ReminderDefinition def, ReminderRegistration reg)
    {
        def.TemplateKey = reg.TemplateKey;
        def.PayloadJson = SerializePayload(reg.Payload);
        def.NotificationType = reg.NotificationType;
        def.DigestKey = reg.DigestKey;
        def.ChannelHint = reg.ChannelHint;

        var schedule = reg.Schedule;
        def.ScheduleType = schedule.Type;

        // Reset every per-type column first so switching schedule type leaves no stale data behind.
        def.DueAt = null;
        def.IntervalPreset = null;
        def.Cron = null;
        def.AnchorDate = null;
        def.EndsAt = null;

        switch (schedule.Type)
        {
            case ReminderScheduleType.OneShot:
                def.DueAt = schedule.DueAt;
                break;
            case ReminderScheduleType.RecurringInterval:
                def.IntervalPreset = schedule.IntervalPreset;
                def.AnchorDate = schedule.AnchorDate;
                def.EndsAt = schedule.EndsAt;
                break;
            case ReminderScheduleType.RecurringCron:
                def.Cron = schedule.Cron;
                def.EndsAt = schedule.EndsAt;
                break;
        }

        // Lead offsets (one-shot only): replace wholesale so a re-register reflects the new offsets.
        def.LeadOffsets.Clear();
        if (schedule.Type == ReminderScheduleType.OneShot && schedule.LeadOffsetsMinutes is not null)
            foreach (var minutes in schedule.LeadOffsetsMinutes)
                def.LeadOffsets.Add(new ReminderLeadOffset { OffsetMinutes = minutes });
    }

    private static void ApplyRecipients(ReminderDefinition def, ReminderRegistration reg)
    {
        def.RecipientMode = reg.RecipientMode;
        def.RecipientResolverKey = reg.RecipientMode == RecipientMode.ResolverStrategy ? reg.RecipientResolverKey : null;

        def.Recipients.Clear();
        if (reg.RecipientMode == RecipientMode.ExplicitUsers && reg.ExplicitRecipientUserIds is not null)
            foreach (var userId in reg.ExplicitRecipientUserIds.Distinct())
                def.Recipients.Add(new ReminderRecipient { UserId = userId });
    }

    // --- Validation (fail fast at registration; ArgumentException → 400 at the endpoint). ---

    private void Validate(ReminderRegistration reg)
    {
        if (string.IsNullOrWhiteSpace(reg.TemplateKey))
            throw new ArgumentException("TemplateKey is required.", nameof(reg));

        ValidateSchedule(reg.Schedule);
        ValidateRecipients(reg);
        ValidateTextResolution(reg);
    }

    private void ValidateSchedule(ReminderSchedule schedule)
    {
        switch (schedule.Type)
        {
            case ReminderScheduleType.OneShot:
                if (schedule.DueAt is null)
                    throw new ArgumentException("A one-shot reminder requires DueAt.");
                var offsets = schedule.LeadOffsetsMinutes ?? [];
                if (offsets.Distinct().Count() != offsets.Count)
                    throw new ArgumentException("Lead offsets must be unique.");
                if (offsets.Any(o => o > 0))
                    throw new ArgumentException("Lead offsets must be <= 0 (before or at the deadline).");
                break;

            case ReminderScheduleType.RecurringInterval:
                if (schedule.IntervalPreset is null || schedule.AnchorDate is null)
                    throw new ArgumentException("A recurring-interval reminder requires an interval preset and an anchor date.");
                if (schedule.EndsAt is { } endsAt && endsAt <= schedule.AnchorDate.Value)
                    throw new ArgumentException("EndsAt must be after AnchorDate.");
                break;

            case ReminderScheduleType.RecurringCron:
                if (!cronEvaluator.IsValid(schedule.Cron ?? string.Empty))
                    throw new ArgumentException($"Invalid cron expression '{schedule.Cron}'.");
                break;
        }
    }

    private void ValidateRecipients(ReminderRegistration reg)
    {
        switch (reg.RecipientMode)
        {
            case RecipientMode.ExplicitUsers:
                if (reg.ExplicitRecipientUserIds is null || reg.ExplicitRecipientUserIds.Count == 0)
                    throw new ArgumentException("ExplicitUsers recipient mode requires a non-empty user id list.");
                break;

            case RecipientMode.ResolverStrategy:
                if (string.IsNullOrWhiteSpace(reg.RecipientResolverKey))
                    throw new ArgumentException("ResolverStrategy recipient mode requires a resolver key.");
                if (recipientResolvers.All(r => r.Key != reg.RecipientResolverKey))
                    throw new ArgumentException($"No recipient resolver is registered for key '{reg.RecipientResolverKey}'.");
                break;
        }
    }

    /// <summary>
    /// At-least-one text path must be satisfiable at registration: a <c>NotificationType</c> is set, or a
    /// keyed <see cref="IReminderRenderer"/> is registered for the template key. Both may coexist (dispatch
    /// uses a fixed precedence in phase 03); only "neither" is rejected.
    /// </summary>
    private void ValidateTextResolution(ReminderRegistration reg)
    {
        if (reg.NotificationType is not null)
            return;
        if (renderers.Any(r => r.TemplateKey == reg.TemplateKey))
            return;

        throw new ArgumentException(
            $"Reminder template '{reg.TemplateKey}' has no text source: set a NotificationType or register an IReminderRenderer for the template key.");
    }
}