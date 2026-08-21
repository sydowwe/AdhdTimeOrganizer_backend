using AdhdTimeOrganizer.History.application.dto.request.timerAlarm;
using AdhdTimeOrganizer.History.domain.serviceContract;
using FastEndpoints;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.History.application.endpoint.command;

/// <summary>
/// Cancels every alarm for one timer session. Called on Stop, pause, discard-and-replace, clear, and by a
/// reload that finds the session already finished.
/// <para>
/// <b>An unknown session is a no-op, never a 404.</b> A partially-registered set is the normal outcome of a
/// tab dying mid-flight, and the client cancels the whole set regardless of what it believes landed — so the
/// "nothing to cancel" case is a success, not an error.
/// </para>
/// <para>
/// The residual failure this does <i>not</i> fix, and deliberately: a tab killed between Stop and this
/// request landing leaves alarms that fire for a session already over. Suppressing that would need the
/// session state readable from the service worker — a second source of truth for the session, mirrored into
/// IndexedDB and kept in step — to avoid a rare, mild, self-explaining notification. Not worth it.
/// </para>
/// </summary>
public class CancelTimerAlarmsEndpoint(ITimerBoundaryAlarmService alarms) : Endpoint<CancelTimerAlarmsRequest>
{
    public override void Configure()
    {
        Post("/activity-history/timer-alarm/cancel");
        Summary(s =>
        {
            s.Summary = "Cancel every alarm registered for one timer session";
            s.Description = "Idempotent. Cancelling an unknown or already-cancelled session succeeds with no content.";
        });
    }

    public override async Task HandleAsync(CancelTimerAlarmsRequest req, CancellationToken ct)
    {
        // Scoped to the caller, which is what makes a guessed session guid useless to anyone else.
        await alarms.CancelSessionAsync(User.GetId(), req.SessionId, ct);

        await Send.NoContentAsync(ct);
    }
}
