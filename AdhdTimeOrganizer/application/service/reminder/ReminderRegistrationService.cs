using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.reminder;
using AdhdTimeOrganizer.domain.model.entity.user;
using AdhdTimeOrganizer.domain.model.@enum;
using AdhdTimeOrganizer.domain.serviceContract;
using AdhdTimeOrganizer.infrastructure.persistence;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.Contracts.notification;
using Sydowwe.Framework.Contracts.notification.payload;
using Sydowwe.Framework.Contracts.reminders;

namespace AdhdTimeOrganizer.application.service.reminder;

/// <summary>
/// Translates a portal <see cref="Reminder"/> row into <see cref="IReminderRegistry"/> calls. This is the
/// only class in the portal that knows the module's key format, schedule shape or payload — everything else
/// calls the <see cref="IReminderRegistrationService"/> methods and stays a one-liner.
/// <para>
/// The portal deliberately does <b>not</b> route users through the module's own
/// <c>RegisterReminderEndpoint</c>: that one is Admin-only and takes a flat registration DTO. Users get the
/// portal's own CRUD surface, and this service is what turns their intent into a registration.
/// </para>
/// </summary>
public class ReminderRegistrationService(
    AppDbContext dbContext,
    IReminderRegistry registry,
    ILogger<ReminderRegistrationService> logger) : IReminderRegistrationService, IScopedService
{
    /// <summary>This portal, as the module sees it. Half of the idempotency key.</summary>
    public const string OwnerModule = "Portal";

    /// <summary>The subject is the portal reminder row itself, so its own id is the module's subject id.</summary>
    public const string SubjectType = nameof(Reminder);

    /// <summary>
    /// One kind for every personal reminder, standalone or task-linked — deliberately not derived from the
    /// link. Two reasons: a reminder that is detached from its task would otherwise change key and strand the
    /// old definition (the key is non-filtered-unique, so the orphan would sit there forever), and a single
    /// kind means the user mutes personal reminders with one <c>ReminderKindPreference</c> row instead of one
    /// per flavour.
    /// </summary>
    public const string Kind = "PersonalReminder";

    /// <summary>Stable content key. Required by the registry even when the text comes from a NotificationType.</summary>
    public const string TemplateKey = "portal.personal-reminder";

    /// <summary>The module's idempotency identity for one portal reminder row.</summary>
    public static ReminderKey KeyFor(long reminderId) => new(OwnerModule, SubjectType, reminderId.ToString(), Kind);

    public async Task SyncAsync(Reminder reminder, CancellationToken ct = default)
    {
        PlannerTask? task = null;
        if (reminder.PlannerTaskId is { } plannerTaskId)
        {
            task = await dbContext.PlannerTasks
                .AsNoTracking()
                .Include(t => t.Calendar)
                .FirstOrDefaultAsync(t => t.Id == plannerTaskId, ct);
        }

        await SyncCoreAsync(reminder, task, null, ct);
    }

    /// <summary>
    /// Shared core of <see cref="SyncAsync"/>, taking an already-loaded task so batch callers
    /// (<see cref="SyncForPlannerTasksAsync"/>) can load every attached task and user timezone once instead
    /// of once per reminder.
    /// </summary>
    /// <param name="timezone">
    /// The reminder owner's time zone when the caller has already loaded it in bulk; <c>null</c> makes this
    /// method fetch it itself. Passing it is what keeps <see cref="SyncForPlannerTasksAsync"/> from issuing
    /// one user lookup per reminder.
    /// </param>
    private async Task SyncCoreAsync(Reminder reminder, PlannerTask? task, TimeZoneInfo? timezone, CancellationToken ct)
    {
        if (reminder.PlannerTaskId is not null)
        {
            // The FK makes `task is null` unreachable in practice; treating it as "nothing to schedule" is
            // still the only safe reading — a reminder pointing at no task has no instant to derive.
            // Decision: finishing a task retires its nudge. A reminder for something already done or called
            // off is pure noise, and the user cancelled it implicitly by acting on the task.
            if (task is null || task.Status is PlannerTaskStatus.Completed or PlannerTaskStatus.Cancelled)
            {
                await CancelAsync(reminder.Id, ct);
                return;
            }

            timezone ??= await ResolveTimezoneAsync(reminder.UserId, ct);
            var derived = ComposeTaskInstant(task.Calendar.Date, task.StartTime, reminder.UserId, timezone);
            if (reminder.RemindAt != derived)
            {
                // RemindAt is a cache of the task's instant so the day view can query one column; the task
                // stays authoritative, which is why it is rewritten on every sync rather than trusted.
                reminder.RemindAt = derived;
                await dbContext.SaveChangesAsync(ct);
            }
        }

        await registry.RegisterAsync(new ReminderRegistration
        {
            Key = KeyFor(reminder.Id),
            Schedule = BuildSchedule(reminder),
            TemplateKey = TemplateKey,
            NotificationType = NotificationType.PersonalReminder,
            Payload = new PersonalReminderPayload(reminder.Id, reminder.Title, reminder.PlannerTaskId),
            // Explicit users, never a resolver strategy: the module's self-service reads (the user's own
            // upcoming list, snooze and dismiss) all exclude resolver-backed reminders, because a read path
            // must never invoke a resolver. A personal reminder has exactly one, known recipient anyway.
            RecipientMode = RecipientMode.ExplicitUsers,
            ExplicitRecipientUserIds = [reminder.UserId]
        }, ct);
    }

    public async Task ApplyUserDefaultsAsync(Reminder reminder, bool leadOffsetsWereSpecified, CancellationToken ct = default)
    {
        // Only an omitted field asks for the default, and only a task-linked reminder has a "before it starts"
        // for the setting to be about. A standalone "call the dentist at 15:00" already names its own moment.
        if (leadOffsetsWereSpecified || reminder.PlannerTaskId is null)
            return;

        var settings = await dbContext.UserPlannerSettings
            .AsNoTracking()
            .Where(s => s.UserId == reminder.UserId)
            .Select(s => new { s.RemindersEnabled, s.ReminderMinutesBefore })
            .FirstOrDefaultAsync(ct);

        // No settings row, reminders not offered, or a nonsensical lead: fall through to the stored [0]. Never
        // to "no reminder" — the user asked for this one explicitly.
        if (settings is not { RemindersEnabled: true, ReminderMinutesBefore: > 0 })
            return;

        reminder.LeadOffsetsMinutes = [-settings.ReminderMinutesBefore];
    }

    /// <summary>
    /// Every call site today sources <paramref name="reminderId"/> from a user-scoped query or a prior
    /// <c>AuthorizeAsync</c> check (see <c>DeleteReminderEndpoint</c>, <c>DeletePlannerTaskEndpoint</c>); this
    /// service is not itself the ownership boundary, so a future call site must not forward a client-supplied
    /// id straight in.
    /// <para>
    /// Callers use this only <b>after</b> the owning portal row has already committed, so a registry failure
    /// here must not throw back into a request that has nothing left to roll back — it is logged loudly
    /// instead, leaving a strandable <c>ReminderDefinition</c> that has to be found and cancelled by hand.
    /// </para>
    /// </summary>
    public async Task CancelAsync(long reminderId, CancellationToken ct = default)
    {
        try
        {
            await registry.CancelAsync(KeyFor(reminderId), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex,
                "Failed to cancel reminder registration for reminder {ReminderId} after its portal row was already deleted; the module's ReminderDefinition is now orphaned and must be cancelled manually",
                reminderId);
        }
    }

    public async Task CancelManyAsync(IReadOnlyCollection<long> reminderIds, CancellationToken ct = default)
    {
        foreach (var reminderId in reminderIds)
            await CancelAsync(reminderId, ct);
    }

    public async Task SyncForPlannerTasksAsync(IReadOnlyCollection<long> plannerTaskIds, CancellationToken ct = default)
    {
        if (plannerTaskIds.Count == 0)
            return;

        var reminders = await dbContext.Reminders
            .Where(r => r.PlannerTaskId != null && plannerTaskIds.Contains(r.PlannerTaskId.Value))
            .ToListAsync(ct);

        if (reminders.Count == 0)
            return;

        var tasks = await dbContext.PlannerTasks
            .AsNoTracking()
            .Include(t => t.Calendar)
            .Where(t => plannerTaskIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, ct);

        // One lookup for the whole batch. Without it SyncCoreAsync fetches the owner's zone per reminder,
        // which is the N+1 the batch loading above exists to avoid.
        var userIds = reminders.Select(r => r.UserId).Distinct().ToList();
        var timezones = await dbContext.Set<User>()
            .AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Timezone })
            .ToDictionaryAsync(u => u.Id, u => u.Timezone ?? TimeZoneInfo.Utc, ct);

        foreach (var reminder in reminders)
        {
            tasks.TryGetValue(reminder.PlannerTaskId!.Value, out var task);
            await SyncCoreAsync(reminder, task, timezones.GetValueOrDefault(reminder.UserId, TimeZoneInfo.Utc), ct);
        }
    }

    public async Task<IReadOnlyList<long>> GetReminderIdsForPlannerTasksAsync(IReadOnlyCollection<long> plannerTaskIds, CancellationToken ct = default)
    {
        if (plannerTaskIds.Count == 0)
            return [];

        return await dbContext.Reminders
            .AsNoTracking()
            .Where(r => r.PlannerTaskId != null && plannerTaskIds.Contains(r.PlannerTaskId.Value))
            .Select(r => r.Id)
            .ToListAsync(ct);
    }

    // --- schedule mapping -------------------------------------------------------------------------

    /// <summary>
    /// One-shot reminders map straight onto the <c>Sydowwe.Framework.Contracts</c> lead-offset schedule. Recurring ones cannot: the
    /// contract's <see cref="ReminderSchedule.RecurringInterval"/> has no lead-offset concept at all, so the
    /// single offset a recurring reminder is allowed to carry is folded into the anchor instead. That is why
    /// <c>ReminderValidator</c> caps a recurring reminder at one offset — with two there would be no way to
    /// express the second.
    /// </summary>
    private static ReminderSchedule BuildSchedule(Reminder reminder)
    {
        var offsets = NormalizeOffsets(reminder.LeadOffsetsMinutes);
        var remindAt = AsUtc(reminder.RemindAt);

        return reminder.Recurrence is { } recurrence
            ? ReminderSchedule.RecurringInterval(ToPreset(recurrence), remindAt.AddMinutes(offsets[0]))
            : ReminderSchedule.OneShot(remindAt, offsets);
    }

    /// <summary>
    /// Offsets as the registry wants them: unique and ordered. Values must be <c>&lt;= 0</c> — that is
    /// enforced at the endpoint validator and again by the registry (which throws), so a positive one is left
    /// to fail loudly here rather than being silently negated into something the user did not ask for.
    /// </summary>
    private static IReadOnlyList<int> NormalizeOffsets(List<int> offsets)
    {
        var normalized = offsets.Distinct().OrderBy(o => o).ToList();
        return normalized.Count > 0 ? normalized : [0];
    }

    private static ReminderIntervalPreset ToPreset(ReminderRecurrence recurrence) => recurrence switch
    {
        ReminderRecurrence.Daily => ReminderIntervalPreset.Daily,
        ReminderRecurrence.Weekly => ReminderIntervalPreset.Weekly,
        ReminderRecurrence.Monthly => ReminderIntervalPreset.Monthly,
        ReminderRecurrence.Quarterly => ReminderIntervalPreset.Quarterly,
        ReminderRecurrence.Yearly => ReminderIntervalPreset.Yearly,
        _ => throw new ArgumentOutOfRangeException(nameof(recurrence), recurrence, "Unmapped reminder recurrence.")
    };

    // --- instant composition ----------------------------------------------------------------------

    /// <summary>
    /// A planner task's reminder instant is <c>Calendar.Date</c> + <c>PlannerTask.StartTime</c> read in the
    /// <b>user's own</b> time zone, then converted to UTC. Composing it in UTC directly would put the nudge
    /// hours off for anyone not living on UTC.
    /// <para>
    /// Note the asymmetry this leaves: quiet hours are evaluated by the Notifications module in the single
    /// deployment zone (<c>Application:Timezone</c>), not per user. Same zone today; they diverge the moment
    /// a user travels.
    /// </para>
    /// </summary>
    private DateTime ComposeTaskInstant(DateOnly date, TimeOnly startTime, long userId, TimeZoneInfo timezone)
    {
        var local = DateTime.SpecifyKind(date.ToDateTime(startTime), DateTimeKind.Unspecified);

        // Spring-forward: the wall-clock time the task claims does not exist that day. Push it to the first
        // instant that does rather than throwing — a DST gap must not make a planner task unsaveable.
        if (timezone.IsInvalidTime(local))
        {
            var shifted = local.Add(timezone.GetAdjustmentRules()
                .FirstOrDefault(r => local.Date >= r.DateStart && local.Date <= r.DateEnd)?.DaylightDelta ?? TimeSpan.FromHours(1));
            logger.LogInformation(
                "Planner task reminder for user {UserId} falls in a DST gap; shifted to {Shifted:HH:mm} local", userId, shifted);
            local = shifted;
        }
        // Fall-back: the wall-clock time occurs twice that day. ConvertTimeToUtc silently resolves it to the
        // earlier (standard-time) occurrence; log so an hour-off nudge has a trail, same as the DST-gap case.
        else if (timezone.IsAmbiguousTime(local))
        {
            logger.LogInformation(
                "Planner task reminder for user {UserId} falls in an ambiguous DST fall-back hour; resolving to the earlier occurrence", userId);
        }

        return TimeZoneInfo.ConvertTimeToUtc(local, timezone);
    }

    private async Task<TimeZoneInfo> ResolveTimezoneAsync(long userId, CancellationToken ct) =>
        await dbContext.Set<User>()
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.Timezone)
            .FirstOrDefaultAsync(ct) ?? TimeZoneInfo.Utc;

    /// <summary>
    /// Reminder instants are absolute. A value off the wire may arrive with an unspecified kind, so pin it to
    /// UTC rather than letting the server's local zone decide (and letting Npgsql reject it).
    /// </summary>
    internal static DateTimeOffset AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => new DateTimeOffset(value),
        DateTimeKind.Local => new DateTimeOffset(value.ToUniversalTime()),
        _ => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc))
    };
}