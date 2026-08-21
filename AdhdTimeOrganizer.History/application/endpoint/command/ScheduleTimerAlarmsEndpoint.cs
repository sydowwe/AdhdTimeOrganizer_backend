using AdhdTimeOrganizer.History.application.dto.request.timerAlarm;
using AdhdTimeOrganizer.History.application.validator;
using AdhdTimeOrganizer.History.domain.serviceContract;
using FastEndpoints;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.History.application.endpoint.command;

/// <summary>
/// Registers the whole alarm set for one timer session. Called once, when the user presses Start.
/// <para>
/// The client posts a timer-domain fact — "this session's boundaries land at these instants" — rather than
/// reaching for the Reminders module's own register endpoint, which is Admin/Root-only diagnostic surface.
/// That is the same split <c>Planning.ReminderRegistrationService</c> already makes: users get the portal's
/// own endpoint, and a service turns their intent into scheduling.
/// </para>
/// <para>
/// <b>Idempotent by session id</b> — re-posting replaces the set rather than conflicting, because a client
/// re-sending after a reload should not have to know what landed the first time.
/// </para>
/// </summary>
public class ScheduleTimerAlarmsEndpoint(ITimerBoundaryAlarmService alarms)
    : Endpoint<ScheduleTimerAlarmsRequest, ScheduleTimerAlarmsResponse>
{
    public override void Configure()
    {
        Post("/activity-history/timer-alarm/schedule");
        Validator<ScheduleTimerAlarmsValidator>();
        Summary(s =>
        {
            s.Summary = "Register the end-of-phase alarms for one timer session";
            s.Description = "Idempotent by sessionId: re-posting replaces the session's alarms. Boundaries "
                            + "already in the past are dropped rather than fired immediately.";
        });
    }

    public override async Task HandleAsync(ScheduleTimerAlarmsRequest req, CancellationToken ct)
    {
        // The recipient is always the caller. A user can only schedule their own timer, so there is no
        // recipient on the request to be spoofed — and the service scopes its cancel-by-session read to this
        // same id, which is what stops one user cancelling another's alarms by guessing a session guid.
        var userId = User.GetId();

        var registered = await alarms.ReplaceSessionAsync(userId, req.SessionId, req.ToEntities(), ct);

        await Send.ResponseAsync(new ScheduleTimerAlarmsResponse { Registered = registered }, cancellation: ct);
    }
}

/// <summary>
/// How many alarms were actually registered — the posted count minus any boundary already in the past. The
/// client does not need it to function (it can rebuild every tag from the instants it already holds), but it
/// makes "why did nothing ring" answerable without reading server logs.
/// </summary>
public record ScheduleTimerAlarmsResponse
{
    public required int Registered { get; init; }
}
