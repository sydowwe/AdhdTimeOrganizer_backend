using AdhdTimeOrganizer.application.dto.response.reminder;
using AdhdTimeOrganizer.domain.model.entity.reminder;
using AdhdTimeOrganizer.domain.model.entity.user;
using AdhdTimeOrganizer.domain.model.@enum;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.application.endpoint.reminder.query;

public record GetRemindersByDateRequest
{
    /// <summary>The day to list, in the caller's own time zone.</summary>
    public required DateOnly Date { get; init; }
}

/// <summary>
/// The day view's reminder list: everything of the caller's that fires on one local day, one-shot and
/// repeating alike, ordered by time.
/// <para>
/// A plain endpoint rather than one of the read bases, for two reasons the bases can't accommodate. The day
/// boundary is a <b>time-zone-dependent instant range</b> that needs the caller's <c>User.Timezone</c> — an
/// awaitable lookup, while <c>BaseGetAllEndpoint.Filter</c> is synchronous. And a repeating reminder's stored
/// <c>RemindAt</c> is its anchor, not the instant it fires today, so a pure SQL range filter over that column
/// would list one-shot reminders and silently drop every recurring one.
/// </para>
/// </summary>
public class GetByDateReminderEndpoint(AppDbContext dbContext) : Endpoint<GetRemindersByDateRequest, List<ReminderOnDateResponse>>
{
    public override void Configure()
    {
        Get("/reminder/by-date/{date}");
        Roles(this.GetUserRole());
        Summary(s =>
        {
            s.Summary = "Get reminders for a day";
            s.Description = "Reminders of the calling user that fire on the given local date, repeating ones expanded to that day";
            s.Response<List<ReminderOnDateResponse>>(200, "Success");
        });
    }

    public override async Task HandleAsync(GetRemindersByDateRequest req, CancellationToken ct)
    {
        var userId = User.GetId();
        var timezone = await dbContext.Set<User>()
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.Timezone)
            .FirstOrDefaultAsync(ct) ?? TimeZoneInfo.Utc;

        var dayStart = ToUtc(req.Date.ToDateTime(TimeOnly.MinValue), timezone);
        var dayEnd = ToUtc(req.Date.AddDays(1).ToDateTime(TimeOnly.MinValue), timezone);

        // Reads go through AppDbContext's user query filter, so "the caller's" is already implied — the
        // narrowing below is about the day, not about ownership.
        var oneShot = await dbContext.Reminders
            .AsNoTracking()
            .Where(r => r.Recurrence == null && r.RemindAt >= dayStart && r.RemindAt < dayEnd)
            .ToListAsync(ct);

        // Repeating ones are filtered to "anchored at or before this day" in SQL, then their cadence is
        // evaluated in memory: whether a Yearly anchor lands on a given local date is calendar arithmetic
        // Postgres would need the user's zone to answer, and a user holds a handful of these at most.
        var repeating = await dbContext.Reminders
            .AsNoTracking()
            .Where(r => r.Recurrence != null && r.RemindAt < dayEnd)
            .ToListAsync(ct);

        var results = oneShot
            .Select(r => ToResponse(r, r.RemindAt))
            .Concat(repeating
                .Select(r => (Reminder: r, Anchor: TimeZoneInfo.ConvertTimeFromUtc(r.RemindAt, timezone)))
                .Where(x => OccursOn(x.Anchor, req.Date, x.Reminder.Recurrence!.Value))
                .Select(x => ToResponse(x.Reminder, ToUtc(req.Date.ToDateTime(TimeOnly.FromDateTime(x.Anchor)), timezone))))
            .OrderBy(r => r.OccursAt)
            .ToList();

        await Send.OkAsync(results, ct);
    }

    /// <summary>
    /// Does a recurrence anchored at <paramref name="anchor"/> (already in the user's zone) land on
    /// <paramref name="date"/>? Answers the day-view question only — "which day does this hit" — which is a
    /// different question from the module's <c>ReminderOccurrenceCalculator</c> ("what is the next occurrence
    /// at or after now, minus what already fired"), so this is not a duplicate of that math.
    /// <para>
    /// Month-end behaviour follows the calculator's <c>AddMonths</c>/<c>AddYears</c> semantics: a 31st anchor
    /// simply has no occurrence in a 30-day month, and 29 February recurs on 29 February only.
    /// </para>
    /// </summary>
    private static bool OccursOn(DateTime anchor, DateOnly date, ReminderRecurrence recurrence)
    {
        var anchorDate = DateOnly.FromDateTime(anchor);
        if (date < anchorDate)
            return false;

        return recurrence switch
        {
            ReminderRecurrence.Daily => true,
            ReminderRecurrence.Weekly => date.DayOfWeek == anchorDate.DayOfWeek,
            ReminderRecurrence.Monthly => date.Day == anchorDate.Day,
            ReminderRecurrence.Quarterly => date.Day == anchorDate.Day && MonthsApart(anchorDate, date) % 3 == 0,
            ReminderRecurrence.Yearly => date.Day == anchorDate.Day && date.Month == anchorDate.Month,
            _ => false
        };
    }

    private static int MonthsApart(DateOnly from, DateOnly to) => (to.Year - from.Year) * 12 + (to.Month - from.Month);

    /// <summary>
    /// Wall-clock in the user's zone to an absolute instant. A spring-forward gap has no such instant, so the
    /// time is nudged past the gap rather than throwing — a missing hour must not 500 the day view.
    /// </summary>
    private static DateTime ToUtc(DateTime local, TimeZoneInfo timezone)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (timezone.IsInvalidTime(local))
            local = local.AddHours(1);

        return TimeZoneInfo.ConvertTimeToUtc(local, timezone);
    }

    private static ReminderOnDateResponse ToResponse(Reminder reminder, DateTime occursAt) => new()
    {
        Id = reminder.Id,
        Title = reminder.Title,
        Note = reminder.Note,
        RemindAt = reminder.RemindAt,
        OccursAt = occursAt,
        LeadOffsetsMinutes = reminder.LeadOffsetsMinutes,
        Recurrence = reminder.Recurrence,
        PlannerTaskId = reminder.PlannerTaskId
    };
}