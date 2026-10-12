using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.Planning.application.helper;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.service;
using AdhdTimeOrganizer.Planning.domain.serviceContract;
using FastEndpoints;
using Sydowwe.Framework.application.dto.dto;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.Planning.application.endpoint.activityPlanning.calendar;

/// <summary>
/// <c>POST /calendar/day-plan/{date}/replan</c> — FO4's preview: what packing the slipped and not-yet-started
/// blocks into the rest of today would do. <b>Writes no planner task</b>; the user applies it with
/// <see cref="ApplyReplanCalendarEndpoint"/>, never automatically. The rule is <see cref="DayReplanPlanner"/>.
/// <para>
/// Only the user's today (their own zone, <c>TimeProvider</c>'s clock) can be re-planned: a past day has no time
/// left, and a future day has nothing that slipped yet. Any other date is a 400.
/// </para>
/// <para>
/// Like <c>GET /calendar/day-plan/{date}</c>, it first lets FO6's materialiser put today's auto-placing repeating
/// blocks on the plan (it commits; a no-op once the day is decided), so the preview packs the day the user sees.
/// </para>
/// </summary>
public class PreviewReplanCalendarEndpoint(
    DbContext dbContext,
    IUserTimeZoneResolver timeZones,
    IRepeatingTaskMaterialiser materialiser,
    TimeProvider timeProvider)
    : EndpointWithoutRequest<DayReplanPreviewResponse>
{
    protected virtual string[] AllowedRoles() => this.GetDefaultRoles();

    public override void Configure()
    {
        Post("/calendar/day-plan/{date}/replan");
        Roles(AllowedRoles());
        Summary(s =>
        {
            s.Summary = "Preview re-planning the rest of today";
            s.Description =
                "Packs today's missed, late and not-yet-started foreground blocks, in their current order, into the free "
                + "time between now and bed time. In-progress, paused, completed and unavoidable blocks stay put and hold "
                + "their time; background blocks neither move nor hold time. Optional blocks give way first; what has no "
                + "room comes back in wontFit. Nothing slipped is a 200 with no moves. Writes no planner task. Date is "
                + "yyyy-MM-dd (dd-MM-yyyy also accepted) and must be the user's today.";
            s.Response<DayReplanPreviewResponse>(200, "The preview — empty when nothing slipped");
            s.Response(400, "Malformed date, or a date that is not the user's today");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        if (!TaskPlannerHelper.TryParseDayPlanRouteDate(Route<string>("date"), out var date))
        {
            AddError("date", "Date must be in yyyy-MM-dd format.");
            await Send.ErrorsAsync(cancellation: ct);
            return;
        }

        var userId = User.GetId();
        var zone = await timeZones.GetAsync(userId, ct);
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        if (date != TaskPlannerHelper.TodayIn(zone, utcNow))
        {
            AddError("date", "Only the user's today can be re-planned.");
            await Send.ErrorsAsync(cancellation: ct);
            return;
        }

        // Before the read: it commits, and what it places is part of the day being packed.
        await materialiser.PlaceForReadAsync(userId, date, ct);

        var (calendar, tasks) = await DayReplanPlanner.LoadDayAsync(dbContext, userId, date, track: false, ct);
        var plan = DayReplanPlanner.Compute(calendar, tasks, date, DayReplanPlanner.PackedFromNow(utcNow, zone));

        // A day with no row has nothing to move, so `Compute` reports the generic window rather than read the user's
        // default for it. The preview still says when the user's own day ends (FO10), the same window
        // `next-free-slot` packs into for such a day.
        var windowEnd = plan.WindowEnd;
        if (calendar is null)
        {
            var sleepWindow = await UserSleepWindow.ForUserAsync(dbContext, userId, ct);
            windowEnd = DayPacking.WindowEnd(sleepWindow.WakeUpTime, sleepWindow.BedTime);
        }

        var ids = plan.Moves.Select(m => m.TaskId).Concat(plan.WontFit.Select(w => w.TaskId)).ToList();
        var responses = ids.Count == 0
            ? new Dictionary<long, PlannerTaskResponse>()
            : await PlannerTaskResponse
                .Projection(dbContext.Set<PlannerTask>().AsNoTracking().Where(t => ids.Contains(t.Id)))
                .ToDictionaryAsync(t => t.Id, ct);

        await Send.OkAsync(new DayReplanPreviewResponse
        {
            Date = date,
            PreviewToken = plan.Token,
            // 1440 only in the last minute of the day; shown as 23:59, which is when the window closes anyway.
            PackedFrom = ToDto(DayPacking.TimeOf(Math.Min(plan.PackedFrom, DayPacking.LastEndMinute))),
            WindowEnd = ToDto(DayPacking.TimeOf(windowEnd)),
            Moves = plan.Moves.Select(m => new DayReplanMoveResponse
            {
                Task = responses[m.TaskId],
                State = m.State,
                StartTime = ToDto(m.ToStart),
                EndTime = ToDto(m.ToEnd)
            }).ToList(),
            WontFit = plan.WontFit.Select(w => new DayReplanWontFitResponse
            {
                Task = responses[w.TaskId],
                State = w.State,
                IsOptional = w.IsOptional
            }).ToList()
        }, ct);
    }

    private static TimeDto ToDto(TimeOnly time) => new(time.Hour, time.Minute);
}
