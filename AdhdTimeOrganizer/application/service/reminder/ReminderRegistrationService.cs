using AdhdTimeOrganizer.domain.model.entity.reminder;
using AdhdTimeOrganizer.domain.model.entity.user;
using AdhdTimeOrganizer.domain.model.@enum;
using AdhdTimeOrganizer.domain.serviceContract;
using AdhdTimeOrganizer.infrastructure.persistence;
using Microsoft.EntityFrameworkCore;
using MojaDigitalnaFirma.Kernel.notification;
using MojaDigitalnaFirma.Kernel.notification.payload;
using MojaDigitalnaFirma.Kernel.reminders;
using Sydowwe.Framework.config.dependencyInjection;

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
        if (reminder.PlannerTaskId is { } plannerTaskId)
        {
            var task = await dbContext.PlannerTasks
                .Include(t => t.Calendar)
                .FirstOrDefaultAsync(t => t.Id == plannerTaskId, ct);

            // The FK makes this unreachable in practice; treating it as "nothing to schedule" is still the
            // only safe reading — a reminder pointing at no task has no instant to derive.
            if (task is null)
            {
                await CancelAsync(reminder.Id, ct);
                return;
            }

            // Decision: finishing a task retires its nudge. A reminder for something already done or called
            // off is pure noise, and the user cancelled it implicitly by acting on the task.
            if (task.Status is PlannerTaskStatus.Completed or PlannerTaskStatus.Cancelled)
            {
                await CancelAsync(reminder.Id, ct);
                return;
            }

            var derived = await ComposeTaskInstantAsync(task.Calendar.Date, task.StartTime, reminder.UserId, ct);
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

    public async Task CancelAsync(long reminderId, CancellationToken ct = default) => await registry.CancelAsync(KeyFor(reminderId), ct);

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

        foreach (var reminder in reminders)
            await SyncAsync(reminder, ct);
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
    /// One-shot reminders map straight onto the Kernel's lead-offset schedule. Recurring ones cannot: the
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
    private async Task<DateTime> ComposeTaskInstantAsync(DateOnly date, TimeOnly startTime, long userId, CancellationToken ct)
    {
        var timezone = await dbContext.Set<User>()
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.Timezone)
            .FirstOrDefaultAsync(ct) ?? TimeZoneInfo.Utc;

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

        return TimeZoneInfo.ConvertTimeToUtc(local, timezone);
    }

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
