using System.Text.Json.Nodes;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.model.entity.reminder;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.Contracts.reminders;
using Sydowwe.Framework.Contracts.user;

namespace AdhdTimeOrganizer.Planning.application.service.reminder;

/// <summary>
/// The safety net under the automatic block reminders (<see cref="ReminderRegistrationService.AutoTemplateKey"/>): at
/// dispatch, "your block starts in 10 min" is delivered only if the reminder row is still there and still automatic,
/// its block still exists, has not been started, finished or skipped, and still starts at the instant the reminder
/// was registered for. Anything else — a block deleted by a cascade nobody synced (an activity delete), moved by a
/// path that forgot to re-sync, or already begun — retires the occurrence, and the scan writes a <c>Skipped</c> row.
/// <para>
/// Stricter than the sync on one point, deliberately: the sync keeps an <c>InProgress</c> block's reminder
/// registered (the user may stop and the block is still the plan), but a block the user already started does not
/// need to be announced.
/// </para>
/// <para>
/// Runs in the background scan with no user: everything is found by the id in the module's key, with the filters
/// lifted, never through an ambient principal. Composes the instant with the sync's own rule
/// (<see cref="ReminderRegistrationService.ComposeTaskInstant(DateOnly, TimeOnly, TimeZoneInfo)"/>), so "moved" means
/// exactly "the sync would register something else now" — a change of the user's time zone included.
/// </para>
/// </summary>
public class PlannedBlockStartingGuard(DbContext dbContext, IUserTimeZoneReader timeZones)
    : IReminderDispatchGuard, IScopedService
{
    public string TemplateKey => ReminderRegistrationService.AutoTemplateKey;

    public async Task<bool> IsStillLiveAsync(ReminderResolutionContext context, CancellationToken ct)
    {
        if (!long.TryParse(context.Key.SubjectId, out var reminderId))
            return false;

        var reminder = await dbContext.Set<Reminder>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(r => r.Id == reminderId)
            .Select(r => new { r.UserId, r.IsAuto, r.PlannerTaskId })
            .FirstOrDefaultAsync(ct);
        if (reminder is not { IsAuto: true, PlannerTaskId: { } taskId })
            return false;

        var task = await dbContext.Set<PlannerTask>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(t => t.Id == taskId)
            .Select(t => new { t.Status, t.StartTime, t.Calendar.Date })
            .FirstOrDefaultAsync(ct);
        if (task is not { Status: PlannerTaskStatus.NotStarted })
            return false;

        var zone = (await timeZones.GetTimeZonesAsync([reminder.UserId], ct)).GetValueOrDefault(reminder.UserId, TimeZoneInfo.Utc);
        var startsAt = ReminderRegistrationService.AsUtc(ReminderRegistrationService.ComposeTaskInstant(task.Date, task.StartTime, zone));

        // A payload with no readable instant cannot be shown stale, and the block is still ahead of the user: deliver.
        return RegisteredStartsAt(context.Payload) is not { } registered || registered == startsAt;
    }

    private static DateTimeOffset? RegisteredStartsAt(JsonNode? payload) =>
        payload is JsonObject obj && obj["startsAt"] is JsonValue value && value.TryGetValue<DateTimeOffset>(out var startsAt)
            ? startsAt
            : null;
}
