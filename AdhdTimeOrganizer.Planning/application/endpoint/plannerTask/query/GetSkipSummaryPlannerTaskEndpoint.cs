using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.Planning.application.dto;
using AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner;
using AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.Planning.application.helper;
using AdhdTimeOrganizer.Planning.application.validator;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.service;
using FastEndpoints;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.Planning.application.endpoint.activityPlanning.plannerTask.query;

/// <summary>
/// <c>POST /planner-task/skip-summary</c> — what the user skipped over a date range, grouped by reason and by
/// planned start hour, in one round trip.
/// <para>
/// A sibling of <see cref="GetPlanVsActualTrendPlannerTaskEndpoint"/>, not an extension of it. The trend's
/// population excludes skipped tasks by design; this one is built from them and their base rate. Putting both
/// in one response would mix two populations with different eligibility rules, and would let a failed skip
/// aggregate cost the month its trend — the client fetches the two independently for that reason.
/// </para>
/// <para>
/// What the two <b>do</b> share is the range: the same request type and validator (same 400s, same
/// <c>MaxSummaryRangeDays</c> ceiling), and the same last-eligible day via
/// <see cref="TaskPlannerHelper.LastDayThatIsOver"/>. Both lines are described as "this period" and must mean
/// the same days. Today is excluded even though a skip made today is already final: today's not-yet-reached
/// tasks would inflate the per-hour denominator.
/// </para>
/// <para>
/// This endpoint owns the eligibility predicate; <see cref="SkipSummaryService"/> owns the grouping and owns no
/// SQL.
/// </para>
/// </summary>
public class GetSkipSummaryPlannerTaskEndpoint(DbContext dbContext, IUserTimeZoneResolver timeZones)
    : Endpoint<PlanVsActualTrendRequest, SkipSummaryResponse>
{
    protected virtual string[] AllowedRoles() => this.GetDefaultRoles();

    public override void Configure()
    {
        Post("/planner-task/skip-summary");
        Roles(AllowedRoles());
        Validator<PlanVsActualTrendValidator>();
        Summary(s =>
        {
            s.Summary = "Skipped planner tasks across a date range, by reason and by planned hour";
            s.Description =
                "Counts skipped (Cancelled) tasks among every non-background planner task in the given inclusive "
                + "date range, on days strictly before the user's today. Reasons are grouped by built-in key when "
                + "there is one, otherwise by exact stored text (neither is its own group, no cap); per planned start hour, returns both the planned and the skipped "
                + "count. SkippedCount of 0 means there was nothing to say, and the caller should render nothing.";
            s.Response<SkipSummaryResponse>(200, "Success");
            s.Response(400, "Bad request — Until before From, or a range over a year");
        });
    }

    public override async Task HandleAsync(PlanVsActualTrendRequest req, CancellationToken ct)
    {
        var userId = User.GetId();
        var timezone = await timeZones.GetAsync(userId, ct);
        var lastEligibleDay = TaskPlannerHelper.LastDayThatIsOver(req.Until, timezone);

        // A range entirely in the future, or entirely today, answers "nothing to say" rather than failing.
        if (lastEligibleDay < req.From)
        {
            await Send.OkAsync(Map(SkipSummaryService.SkipSummary.Empty), ct);
            return;
        }

        // UserId by hand, as in the trend: this slice cannot see the host's global IEntityWithUser filter.
        var rows = await dbContext.Set<PlannerTask>()
            .AsNoTracking()
            .Where(t => t.UserId == userId
                        && t.Calendar.Date >= req.From
                        && t.Calendar.Date <= lastEligibleDay
                        // Background tasks are scaffolding, not commitments — excluded from both the skips and
                        // the per-hour denominator, as in the trend and the single-day line.
                        && !t.IsBackground)
            // Every status on purpose — unlike the trend, Cancelled is the numerator here and the rest are the
            // base rate. Three columns, no navigations.
            .Select(t => new { t.StartTime, t.Status, t.SkipReason, t.SkipReasonKey })
            .ToListAsync(ct);

        var summary = SkipSummaryService.Aggregate(rows.Select(r =>
            new SkipSummaryService.SkipSummaryTaskRow(r.StartTime, r.Status, r.SkipReason, r.SkipReasonKey)));

        await Send.OkAsync(Map(summary), ct);
    }

    private static SkipSummaryResponse Map(SkipSummaryService.SkipSummary summary) =>
        new()
        {
            SkippedCount = summary.SkippedCount,
            Reasons = summary.Reasons
                .Select(r => new SkipReasonCountResponse
                {
                    Reason = r.Reason,
                    ReasonKey = SkipReasonKeyTokens.Token(r.ReasonKey),
                    Count = r.Count
                })
                .ToList(),
            ByStartHour = summary.ByStartHour
                .Select(h => new SkipHourCountResponse
                {
                    Hour = h.Hour,
                    PlannedCount = h.PlannedCount,
                    SkippedCount = h.SkippedCount
                })
                .ToList()
        };
}
