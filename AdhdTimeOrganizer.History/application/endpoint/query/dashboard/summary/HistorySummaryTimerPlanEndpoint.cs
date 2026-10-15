using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.History.application.dto.request.activityHistory.dashboard.summary;
using AdhdTimeOrganizer.History.application.dto.response.activityHistory.dashboard;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.History.domain.model.@enum;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.History.application.endpoint.activityHistory.activityHistory.query.dashboard.summary;

/// <summary>
/// The timer plan against the focus it got, over a range (OP6's trend line): how much focus the range's timers were
/// set for, how much the rows hold, and how the sessions ended. The per-session plan is on the Detail view; this is
/// the range's sum of it.
/// </summary>
/// <remarks>
/// <para>
/// <b>A session belongs to the day its first row starts</b>, on the user's clock, through the same
/// <see cref="Core.application.dto.dto.DateRangeDto.ToUtcRange"/> bounds every <c>summary/</c> read uses. A
/// Pomodoro crossing the range's last midnight is counted whole, as the time-of-day read counts a row's tail.
/// </para>
/// <para>
/// <b>Focus is told from break by <see cref="ActivityHistory.Phase"/>, never by activity</b>: a countdown's one row
/// has no phase and a Pomodoro's focus rows are <see cref="TimerSessionPhase.Focus"/>. The rest activity can be the
/// focus activity, so matching on the activity would count every break as focus.
/// </para>
/// <para>
/// <b><see cref="TimerSessionOutcome.RanOutWhileAway"/> sessions are counted, and kept out of both focus sums.</b>
/// The timer ran while nobody was there. <c>TimerLengthFit</c> drops them whole for the same reason; here they keep
/// their count so the client can name them, but summing their rows would make an abandoned Pomodoro read as a plan
/// met. So the two sums are over the completed and stopped-early sessions, the same sessions on both sides.
/// </para>
/// <para>
/// The outcome and the plan are taken as the timer recorded them and the focus is the rows as they are now. Editing
/// a row moves the focus sum and nothing else; the two are not reconciled. Only a stopped-early session adds to the
/// shortfall: a completed countdown's row can sit a minute under its plan from whole-minute rounding alone.
/// </para>
/// <para>
/// Filtered on the caller. <c>PlannedLength</c> and <c>Length</c> are <c>IntTime</c> behind converters, so their
/// seconds are summed here rather than in SQL, as in <c>GetFitActivityHistorySessionEndpoint</c>.
/// </para>
/// </remarks>
public class HistorySummaryTimerPlanEndpoint(DbContext db, IUserTimeZoneResolver timeZones)
    : Endpoint<HistorySummaryTimerPlanRequest, HistoryTimerPlanResponse>
{
    public override void Configure()
    {
        Post("/activity-history/dashboard/summary/timer-plan");
        Summary(s =>
        {
            s.Summary = "The focus the range's timers were set for, the focus they got, and how they ended";
            s.Response<HistoryTimerPlanResponse>(200, "The range's totals, zeros when it holds no timer session");
            s.Response(400, "The range is invalid");
        });
    }

    public override async Task HandleAsync(HistorySummaryTimerPlanRequest req, CancellationToken ct)
    {
        var userId = User.GetId();
        var timeZone = await timeZones.GetAsync(userId, ct);
        var (from, to) = req.ToUtcRange(timeZone);

        var sessions = await db.Set<ActivityHistorySession>()
            .AsNoTracking()
            .Where(s => s.UserId == userId
                        && s.Records.Min(r => r.StartTimestamp) >= from
                        && s.Records.Min(r => r.StartTimestamp) < to)
            .Select(s => new
            {
                s.Outcome,
                s.PlannedLength,
                Focus = s.Records
                    .Where(r => r.Phase == null || r.Phase == TimerSessionPhase.Focus)
                    .Select(r => r.Length)
                    .ToList()
            })
            .ToListAsync(ct);

        int sessionCount = 0, withoutPlan = 0, completed = 0, stoppedEarly = 0, ranOutWhileAway = 0;
        long planned = 0, actual = 0, shortfall = 0;

        foreach (var session in sessions)
        {
            if (session.PlannedLength is not { } plan)
            {
                withoutPlan++;
                continue;
            }

            sessionCount++;
            switch (session.Outcome)
            {
                case TimerSessionOutcome.RanOutWhileAway:
                    ranOutWhileAway++;
                    continue;
                case TimerSessionOutcome.StoppedEarly:
                    stoppedEarly++;
                    break;
                default:
                    completed++;
                    break;
            }

            long plannedSeconds = plan.TotalSeconds;
            var focusSeconds = session.Focus.Sum(l => (long)l.TotalSeconds);
            planned += plannedSeconds;
            actual += focusSeconds;
            if (session.Outcome == TimerSessionOutcome.StoppedEarly)
                shortfall += Math.Max(0, plannedSeconds - focusSeconds);
        }

        await Send.ResponseAsync(new HistoryTimerPlanResponse
        {
            SessionCount = sessionCount,
            WithoutPlanCount = withoutPlan,
            PlannedFocusSeconds = planned,
            ActualFocusSeconds = actual,
            CompletedCount = completed,
            StoppedEarlyCount = stoppedEarly,
            RanOutWhileAwayCount = ranOutWhileAway,
            StoppedEarlyShortfallSeconds = shortfall
        }, cancellation: ct);
    }
}
