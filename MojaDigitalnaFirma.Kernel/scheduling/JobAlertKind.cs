namespace MojaDigitalnaFirma.Kernel.scheduling;

/// <summary>
/// Which failure mode a <see cref="JobFailureAlert"/> reports. The two are opposites — an <b>error</b> versus
/// <b>silence</b> — and a consumer is expected to surface them as distinct kinds so an operator can triage
/// them apart (Core.Notifications maps each to its own <c>NotificationType</c>).
/// <para>
/// The discriminator is an explicit member rather than something inferred from
/// <see cref="JobFailureAlert.RunId"/> being null: "no run row" is a <i>consequence</i> of the overdue mode,
/// not its definition, and a consumer branching on a nullable id would silently mis-classify the day a
/// third mode is added.
/// </para>
/// </summary>
public enum JobAlertKind
{
    /// <summary>A run happened and failed terminally — final retry, unarmable retry, or <c>HandlerNotFound</c>.</summary>
    TerminalFailure,

    /// <summary>
    /// No run happened at all: an <c>Active</c> job is past its expected fire time by more than the sweep's
    /// margin (scheduler follow-up 08). Raised by the overdue sweep, never by the dispatcher.
    /// </summary>
    Overdue
}