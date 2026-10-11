using AdhdTimeOrganizer.Planning.domain.model.@enum;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.model.entity.reminder;
using AdhdTimeOrganizer.Planning.domain.serviceContract;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.Contracts.user;

namespace AdhdTimeOrganizer.Planning.application.service.reminder;

/// <summary>
/// See <see cref="IPlannedBlockAutoReminders"/> for the contract. The rules it keeps:
/// <list type="bullet">
/// <item><b>Eligible</b>: foreground (a background block is not a commitment to be called to — the same reading as
/// the Google push), <c>NotStarted</c>, and starting in the future on its owner's clock.</item>
/// <item><b>Mode</b> (<see cref="Qualifies"/>): <c>Off</c> creates nothing; <c>ImportantOnly</c> wants the block's
/// importance at or above <c>AutoReminderMinImportance</c>, and never the Optional level; <c>All</c> takes every
/// eligible block. A user with no settings row gets the entity's defaults — the row is only created on the first
/// read of the settings.</item>
/// <item><b>Lead</b>: <c>ReminderMinutesBefore</c>, the same number that prefills the reminder form. Not
/// <c>RemindersEnabled</c>: that is a preference about the form, not about these. When the lead has already passed
/// (a block created eight minutes before it starts), the reminder fires at the start rather than not at all.</item>
/// <item><b>Once</b>: a block that already has any reminder gets no second one.</item>
/// </list>
/// <para>
/// Scoped by hand (<c>IgnoreQueryFilters()</c> + the block's own <c>UserId</c>), the same as the materialiser it
/// serves: in the scheduled sweep there is no signed-in user, and <c>BaseWithUserEntitySaveChangesAsync</c> cannot
/// fill an owner it does not know — an insert relying on it would die on the FK with <c>UserId == 0</c>. The ids
/// come from a save the caller just made, so lifting the filters widens nothing.
/// </para>
/// </summary>
public class PlannedBlockAutoReminders(
    DbContext dbContext,
    IReminderRegistrationService reminders,
    IUserTimeZoneReader timeZones,
    TimeProvider time,
    ILogger<PlannedBlockAutoReminders> logger) : IPlannedBlockAutoReminders, IScopedService
{
    public async Task CreateForNewBlocksAsync(IReadOnlyCollection<long> plannerTaskIds, CancellationToken ct = default)
    {
        if (plannerTaskIds.Count == 0)
            return;

        var created = new List<Reminder>();
        try
        {
            created = await StageAsync(plannerTaskIds.Distinct().ToList(), ct);
            if (created.Count == 0)
                return;

            await dbContext.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Only ours: the caller's own entities are already saved and may still be read for its response.
            foreach (var reminder in created)
                dbContext.Entry(reminder).State = EntityState.Detached;

            logger.LogError(ex, "Failed to create automatic reminders for {Count} new planner task(s); they have none", plannerTaskIds.Count);
            return;
        }

        // After the save, never before — the registration is keyed on the row's id. A registry failure is logged
        // inside and the block keeps its row, so its next move or status change re-registers it.
        await reminders.SyncForPlannerTasksAsync(created.Select(r => r.PlannerTaskId!.Value).ToList(), ct);

        logger.LogInformation("Created {Count} automatic reminder(s) for new planner tasks", created.Count);
    }

    private async Task<List<Reminder>> StageAsync(List<long> taskIds, CancellationToken ct)
    {
        var blocks = await dbContext.Set<PlannerTask>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(t => taskIds.Contains(t.Id))
            .Select(t => new Block(
                t.Id, t.UserId, t.ActivityId, t.IsBackground, t.Status, t.Calendar.Date, t.StartTime,
                t.Importance == null ? null : t.Importance.Importance))
            .ToListAsync(ct);

        if (blocks.Count == 0)
            return [];

        var alreadyReminded = await dbContext.Set<Reminder>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(r => r.PlannerTaskId != null && taskIds.Contains(r.PlannerTaskId.Value))
            .Select(r => r.PlannerTaskId!.Value)
            .ToHashSetAsync(ct);

        var userIds = blocks.Select(b => b.UserId).Distinct().ToList();
        var rules = await dbContext.Set<UserPlannerSettings>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(s => userIds.Contains(s.UserId))
            .Select(s => new Rule(s.UserId, s.AutoReminderMode, s.AutoReminderMinImportance, s.ReminderMinutesBefore))
            .ToDictionaryAsync(s => s.UserId, ct);

        var candidates = blocks
            .Where(b => !alreadyReminded.Contains(b.Id) && Qualifies(RuleFor(rules, b.UserId), b))
            .ToList();
        if (candidates.Count == 0)
            return [];

        var zones = await timeZones.GetTimeZonesAsync(userIds, ct);
        var names = await ReminderRegistrationService.ActivityNamesAsync(
            dbContext, candidates.Select(b => b.ActivityId).Distinct().ToList(), ct);
        var now = time.GetUtcNow().UtcDateTime;

        var staged = new List<Reminder>(candidates.Count);
        foreach (var block in candidates)
        {
            var startsAt = ReminderRegistrationService.ComposeTaskInstant(
                block.Date, block.StartTime, zones.GetValueOrDefault(block.UserId, TimeZoneInfo.Utc));
            if (startsAt <= now)
                continue;

            var leadMinutes = RuleFor(rules, block.UserId).LeadMinutes;
            var lead = leadMinutes > 0 && startsAt.AddMinutes(-leadMinutes) > now ? leadMinutes : 0;

            var reminder = new Reminder
            {
                // Explicit: in the sweep nobody is signed in to stamp it.
                UserId = block.UserId,
                Title = names.GetValueOrDefault(block.ActivityId) ?? string.Empty,
                RemindAt = startsAt,
                LeadOffsetsMinutes = [-lead],
                PlannerTaskId = block.Id,
                IsAuto = true
            };
            dbContext.Set<Reminder>().Add(reminder);
            staged.Add(reminder);
        }

        return staged;
    }

    private static Rule RuleFor(Dictionary<long, Rule> rules, long userId) =>
        rules.GetValueOrDefault(userId) ?? Rule.Defaults(userId);

    /// <summary>Whether a block gets an automatic reminder under its owner's rule — everything but the clock.</summary>
    internal static bool Qualifies(Rule rule, Block block) =>
        !block.IsBackground
        && block.Status == PlannerTaskStatus.NotStarted
        && rule.Mode switch
        {
            AutoReminderMode.All => true,
            AutoReminderMode.ImportantOnly => block.Importance is { } importance
                                              && importance != TaskImportance.OptionalMarkerValue
                                              && importance >= rule.MinImportance,
            _ => false
        };

    internal sealed record Block(
        long Id, long UserId, long ActivityId, bool IsBackground, PlannerTaskStatus Status,
        DateOnly Date, TimeOnly StartTime, int? Importance);

    /// <summary>The owner's settings this needs, or <see cref="Defaults"/> for a user who has no settings row yet.</summary>
    internal sealed record Rule(long UserId, AutoReminderMode Mode, int MinImportance, int LeadMinutes)
    {
        public static Rule Defaults(long userId)
        {
            var defaults = new UserPlannerSettings { UserId = userId };
            return new Rule(userId, defaults.AutoReminderMode, defaults.AutoReminderMinImportance, defaults.ReminderMinutesBefore);
        }
    }
}
