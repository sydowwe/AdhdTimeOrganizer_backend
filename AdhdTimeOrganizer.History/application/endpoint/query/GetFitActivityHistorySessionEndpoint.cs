using AdhdTimeOrganizer.History.application.dto.request.history;
using AdhdTimeOrganizer.History.application.dto.response.activityHistory;
using AdhdTimeOrganizer.History.application.validator;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.History.domain.model.@enum;
using AdhdTimeOrganizer.History.domain.service;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.History.application.endpoint.activityHistory.activityHistory.query;

/// <summary>
/// F2-07: a focus length that usually works for one activity, from how its timer sessions ended. The rule is
/// <see cref="TimerLengthFit"/>.
/// </summary>
/// <remarks>
/// <para>
/// The first reader of <see cref="ActivityHistorySession.Outcome"/> and
/// <see cref="ActivityHistorySession.CompletedFocusPeriods"/> outside the export (C5 stored them, OP6 added the
/// plan). It reads the newest <see cref="TimerLengthFit.SessionLookback"/> sessions of the activity in the last
/// <see cref="TimerLengthFit.WindowDays"/> days, and only their focus rows of that activity.
/// </para>
/// <para>
/// Another user's activity id answers like an activity with no sessions: every read is filtered on the caller, so
/// there is nothing to leak and nothing to tell apart. Computed on read: twenty sessions at most.
/// </para>
/// </remarks>
public class GetFitActivityHistorySessionEndpoint(DbContext db, TimeProvider time)
    : Endpoint<ActivityHistorySessionFitRequest, ActivityHistorySessionFitResponse>
{
    public override void Configure()
    {
        Get("/activity-history/session/fit");
        Roles(this.GetDefaultRoles());
        Validator<ActivityHistorySessionFitValidator>();
        Summary(s =>
        {
            s.Summary = "A focus length that usually works for an activity, from its recent timer sessions";
            s.Response<ActivityHistorySessionFitResponse>(200, "The length, or nulls when there are too few sessions");
            s.Response(400, "activityId missing");
        });
    }

    public override async Task HandleAsync(ActivityHistorySessionFitRequest req, CancellationToken ct)
    {
        var userId = User.GetId();
        var since = time.GetUtcNow().UtcDateTime.AddDays(-TimerLengthFit.WindowDays);
        var activityId = req.ActivityId;

        // Focus rows only. A Pomodoro's breaks may be logged against this same activity, so the phase tells them
        // apart, not the activity. A countdown's one row has no phase.
        var sessions = await db.Set<ActivityHistorySession>()
            .AsNoTracking()
            .Where(s => s.UserId == userId
                        && s.Outcome != TimerSessionOutcome.RanOutWhileAway
                        && s.Records.Any(r => r.ActivityId == activityId
                                              && (r.Phase == null || r.Phase == TimerSessionPhase.Focus))
                        && s.Records.Min(r => r.StartTimestamp) >= since)
            .OrderByDescending(s => s.Records.Min(r => r.StartTimestamp))
            .Take(TimerLengthFit.SessionLookback)
            .Select(s => new
            {
                s.Kind,
                s.Outcome,
                s.CompletedFocusPeriods,
                Focus = s.Records
                    .Where(r => r.ActivityId == activityId
                                && (r.Phase == null || r.Phase == TimerSessionPhase.Focus))
                    .OrderBy(r => r.StartTimestamp)
                    .Select(r => r.Length)
                    .ToList()
            })
            .ToListAsync(ct);

        // Length is an IntTime behind a converter, so its TotalSeconds is read here rather than in SQL.
        var perSession = sessions
            .Select(s => TimerLengthFit.StretchesOf(
                s.Kind, s.Outcome, s.CompletedFocusPeriods, s.Focus.Select(l => (long)l.TotalSeconds).ToList()))
            .Where(stretches => stretches.Count > 0)
            .ToList();

        var fit = TimerLengthFit.Suggest(perSession.SelectMany(s => s).ToList(), perSession.Count);

        await Send.ResponseAsync(new ActivityHistorySessionFitResponse
        {
            SuggestedSeconds = fit?.SuggestedSeconds,
            SampleCount = fit?.SampleCount
        }, cancellation: ct);
    }
}
