namespace MojaDigitalnaFirma.Kernel.notification;

/// <summary>
/// A user's quiet-hours window as a half-open minute-of-day span <c>[StartMinute, EndMinute)</c> in the
/// <b>deployment-configured time zone</b> (<c>Application:Timezone</c>) — the repo models no per-user time
/// zone. <c>Start &gt; End</c> denotes an overnight window that wraps past midnight (e.g. 22:00→06:00);
/// <c>Start == End</c> is degenerate and treated as "no quiet hours" by <see cref="QuietHoursPolicy"/>
/// (the write endpoint rejects it outright).
/// <para>
/// The window is owned by the Notifications module (<c>NotificationQuietHours</c>); other modules read it
/// through <see cref="IQuietHoursReader"/> and never touch the table.
/// </para>
/// </summary>
public readonly record struct QuietHoursWindow(int StartMinute, int EndMinute);