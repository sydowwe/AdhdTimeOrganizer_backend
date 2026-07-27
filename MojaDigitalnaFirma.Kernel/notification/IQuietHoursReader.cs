namespace MojaDigitalnaFirma.Kernel.notification;

/// <summary>
/// Cross-module read seam over the per-user quiet-hours windows owned by the Notifications module.
/// <para>
/// There is exactly <b>one</b> quiet-hours window per user in the deployment, and Notifications owns it —
/// consumers (today: the Reminders scan, which defers a due occurrence when every recipient is inside their
/// window) depend on this contract rather than on <c>Core.Notifications</c>, keeping the dependency arrow
/// pointing into the Kernel. Mirrors how <see cref="INotificationService"/> / <see cref="INotificationPayloadEnricher"/>
/// expose the module's other capabilities.
/// </para>
/// <para>
/// Read-only on purpose: the window is edited through the Notifications module's own owner-scoped endpoints.
/// A host that ships a consumer without Notifications registers a no-op returning no windows, which reads as
/// "nobody has quiet hours" — the safe default, since absence means deliver immediately.
/// </para>
/// </summary>
public interface IQuietHoursReader
{
    /// <summary>
    /// The quiet-hours windows for the given users. Users with no window are <b>absent</b> from the result —
    /// absence means "no quiet hours", never an all-day window.
    /// </summary>
    Task<IReadOnlyDictionary<long, QuietHoursWindow>> GetWindowsAsync(
        IReadOnlyCollection<long> userIds, CancellationToken ct = default);
}