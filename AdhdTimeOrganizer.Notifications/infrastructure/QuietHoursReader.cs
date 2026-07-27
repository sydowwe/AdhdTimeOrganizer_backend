using AdhdTimeOrganizer.Notifications.domain.entity;
using Microsoft.EntityFrameworkCore;
using MojaDigitalnaFirma.Kernel.notification;
using Sydowwe.Framework.config.dependencyInjection;

namespace AdhdTimeOrganizer.Notifications.infrastructure;

/// <summary>
/// The live <see cref="IQuietHoursReader"/> — a straight <c>AsNoTracking</c> read of the module's own
/// <see cref="NotificationQuietHours"/> rows. Auto-registered by the blanket <c>IScopedService</c> scan, so a
/// consumer's no-op fallback (registered with <c>TryAdd</c>) only engages on a host that ships no
/// Notifications module.
/// <para>
/// Background-safe: no authenticated user is required, and only user ids cross the seam — never a window
/// belonging to somebody the caller may not see, since callers pass the recipient ids they already hold.
/// </para>
/// </summary>
public class QuietHoursReader(DbContext dbContext) : IQuietHoursReader, IScopedService
{
    public async Task<IReadOnlyDictionary<long, QuietHoursWindow>> GetWindowsAsync(
        IReadOnlyCollection<long> userIds, CancellationToken ct = default)
    {
        var distinctIds = userIds.Distinct().ToList();
        if (distinctIds.Count == 0)
            return new Dictionary<long, QuietHoursWindow>();

        var rows = await dbContext.Set<NotificationQuietHours>().AsNoTracking()
            .Where(q => distinctIds.Contains(q.UserId))
            .Select(q => new { q.UserId, q.StartMinute, q.EndMinute })
            .ToListAsync(ct);

        // Users with no row are deliberately absent — absence means "no quiet hours".
        return rows.ToDictionary(r => r.UserId, r => new QuietHoursWindow(r.StartMinute, r.EndMinute));
    }
}