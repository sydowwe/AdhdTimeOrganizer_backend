using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner;
using AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.Planning.application.helper;
using AdhdTimeOrganizer.Planning.application.validator;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using AdhdTimeOrganizer.Planning.domain.service;
using FastEndpoints;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.Planning.application.endpoint.activityPlanning.plannerTask.query;

/// <summary>
/// <c>POST /planner-task/plan-vs-actual-trend</c> — how the user's planned time compared with their logged
/// time across a date range, in one round trip.
/// <para>
/// The single-day version of this needs no server at all: the day view already holds
/// <c>ActualStartTime</c>/<c>ActualEndTime</c> beside <c>StartTime</c>/<c>EndTime</c> for the day on screen.
/// The <i>trend</i> cannot be answered that way — <c>planner-task/filter</c> takes one calendar id, so thirty
/// days of it is thirty requests, which is the fan-out this module has been removing elsewhere. Nor can the
/// history dashboards answer it: they aggregate <c>ActivityHistory</c> (free-standing tracking), which has no
/// plan to drift from.
/// </para>
/// <para>
/// This endpoint owns the <b>eligibility predicate</b> — which tasks the trend is built from — and nothing
/// else; <see cref="PlanVsActualTrendService"/> owns the arithmetic and the rules, and owns no SQL. Keep the
/// split: a second place deciding what an eligible task is, is how the two ends of this feature start
/// disagreeing about the same six numbers.
/// </para>
/// </summary>
public class GetPlanVsActualTrendPlannerTaskEndpoint(DbContext dbContext, IUserTimeZoneResolver timeZones)
    : Endpoint<PlanVsActualTrendRequest, PlanVsActualTrendResponse>
{
    protected virtual string[] AllowedRoles() => this.GetDefaultRoles();

    public override void Configure()
    {
        Post("/planner-task/plan-vs-actual-trend");
        Roles(AllowedRoles());
        Validator<PlanVsActualTrendValidator>();
        Summary(s =>
        {
            s.Summary = "Plan-vs-actual drift across a date range";
            s.Description =
                "Aggregates planned against logged time for every eligible planner task in the given inclusive "
                + "date range: not background, not cancelled, and on a day that is materially over. Returns six "
                + "counters; TaskCount of 0 means there was nothing to say, and the caller should render nothing.";
            s.Response<PlanVsActualTrendResponse>(200, "Success");
            s.Response(400, "Bad request — Until before From, or a range over a year");
        });
    }

    public override async Task HandleAsync(PlanVsActualTrendRequest req, CancellationToken ct)
    {
        var userId = User.GetId();

        // The day boundary is the user's own midnight, exactly as PlannerStreakReader takes it — a server or
        // UTC boundary would let a Bratislava user's evening count as tomorrow and pull an unfinished day into
        // the aggregate.
        var timezone = await timeZones.GetAsync(userId, ct);

        // "Materially over" is resolved as *strictly before the user's today*, and today is excluded however
        // late in the day the request arrives. This is stricter than the single-day block, which shows for the
        // viewed day once it is mostly done, and deliberately so: a partial today contributes tasks that have
        // simply not come around yet, and every one of them lands in NeverHappenedCount — dragging a
        // thirty-day trend by a day that has not failed at anything. Shared with the skip summary, which the
        // client renders beside this and which must cover the same days.
        var lastEligibleDay = TaskPlannerHelper.LastDayThatIsOver(req.Until, timezone);

        // No rows can qualify once the range starts after the last day that is over — including a range
        // entirely in the future, which must answer "nothing to say" rather than fail.
        if (lastEligibleDay < req.From)
        {
            await Send.OkAsync(Map(default), ct);
            return;
        }

        // UserId by hand as well as through the host's global IEntityWithUser filter: this slice is compiled
        // against a plain DbContext and cannot see that filter, and the user id is already in hand for the
        // timezone above. Cost is nil — (UserId, CalendarId, StartTime) is the index the scan already rides.
        var rows = await dbContext.Set<PlannerTask>()
            .AsNoTracking()
            .Where(t => t.UserId == userId
                        && t.Calendar.Date >= req.From
                        && t.Calendar.Date <= lastEligibleDay
                        // Background tasks are scaffolding, not commitments — they carry no drift worth
                        // reporting, and the single-day block excludes them for the same reason.
                        && !t.IsBackground
                        // A skip leaves the population entirely rather than counting as a task that never
                        // happened. The app already offers skipping as a legitimate way to close a task; the
                        // streak makes the same call, and the two must not diverge.
                        && t.Status != PlannerTaskStatus.Cancelled)
            // A handful of scalar columns, no entity materialisation: a year of a heavy user's planning is a few
            // thousand of these, which is why the aggregate can afford to be derived on every read.
            .Select(t => new
            {
                t.StartTime,
                t.EndTime,
                t.Status,
                t.ActualStartTime,
                t.ActualEndTime,
                t.Calendar.Date,
                t.OriginalDate,
                t.OriginalStartTime
            })
            .ToListAsync(ct);

        // Lateness is measured from the start the block was first planned for while it is still on that day, so
        // snoozing an overdue block does not make starting it at the snoozed time read as on time (FO3).
        var trend = PlanVsActualTrendService.Aggregate(rows.Select(r =>
            new PlanVsActualTrendService.PlanVsActualTaskRow(
                r.StartTime, r.EndTime, r.Status, r.ActualStartTime, r.ActualEndTime,
                PlannerTaskPostponeRule.DriftAnchor(r.Date, r.StartTime, r.OriginalDate, r.OriginalStartTime))));

        // Slips, by the day each task was first planned for — a separate population from the one above, which
        // is keyed on the day a task is on now. A carried task left its original day, so counting it in
        // NeverHappenedCount as well would count one slip twice. Wherever it is now, background tasks aside.
        var carriedOverCount = await dbContext.Set<PlannerTask>()
            .AsNoTracking()
            .Where(t => t.UserId == userId
                        && t.OriginalDate != null
                        && t.OriginalDate >= req.From
                        && t.OriginalDate <= lastEligibleDay
                        && !t.IsBackground)
            .SumAsync(t => t.CarryOverCount, ct);

        await Send.OkAsync(Map(trend) with { CarriedOverCount = carriedOverCount }, ct);
    }

    private static PlanVsActualTrendResponse Map(PlanVsActualTrendService.PlanVsActualTrend trend) =>
        new()
        {
            TaskCount = trend.TaskCount,
            PlannedMinutes = trend.PlannedMinutes,
            ActualMinutes = trend.ActualMinutes,
            StartedLateCount = trend.StartedLateCount,
            RanLongerCount = trend.RanLongerCount,
            NeverHappenedCount = trend.NeverHappenedCount
        };
}
