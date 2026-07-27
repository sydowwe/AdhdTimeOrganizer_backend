using MojaDigitalnaFirma.Kernel.notification;

namespace AdhdTimeOrganizer.Reminders.infrastructure;

/// <summary>
/// Fallback <see cref="IQuietHoursReader"/> for a host that ships this module <b>without</b> the Notifications
/// module that owns the quiet-hours table. Returns no windows, which reads as "nobody has quiet hours" — the
/// safe default, since absence means deliver immediately rather than defer forever.
/// <para>
/// Deliberately <b>not</b> marked <see cref="Sydowwe.Framework.config.dependencyInjection.IScopedService"/>:
/// an auto-registered no-op could win over the real reader and silently disable quiet hours everywhere. It is
/// wired via <c>TryAddScoped</c> in the host instead, so the live reader always takes precedence.
/// </para>
/// </summary>
public sealed class NoQuietHoursReader : IQuietHoursReader
{
    public Task<IReadOnlyDictionary<long, QuietHoursWindow>> GetWindowsAsync(
        IReadOnlyCollection<long> userIds, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<long, QuietHoursWindow>>(new Dictionary<long, QuietHoursWindow>());
}