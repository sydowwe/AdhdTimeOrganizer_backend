using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.infrastructure.persistence;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Contracts.notification;

namespace AdhdTimeOrganizer.application.service.welcomeBack;

/// <summary>
/// FO5's back-off: tells the notification dispatcher which recipients are away, by the same rule the rest of FO5
/// uses (<see cref="UserPresence.IsAbsent"/>, three days since <see cref="User.LastActiveAt"/>). The dispatcher
/// then keeps the kinds listed in <c>HeldWhileAwayTypes</c> (the routine nudges) in the bell and off their phone.
/// <para>
/// A user never measured is not away, for the reason <see cref="UserPresence.IsAbsent"/> gives. Tracker traffic
/// never moves the stamp (<c>LastActiveStampMiddleware</c>), so a user whose desktop tracker is running all
/// week is still away from the app, which is the point.
/// </para>
/// <para>
/// Registered by hand in <c>ModuleServiceExtensions</c> rather than by marker: the dispatcher takes the reader as
/// an <b>optional</b> dependency, so a missing registration is not an error anywhere, only nudges pushed to people
/// who are not there. <c>ModuleWiringTests</c> pins it.
/// </para>
/// </summary>
public sealed class RecipientEngagementReader(AppDbContext db) : IRecipientEngagementReader
{
    public async Task<IReadOnlySet<long>> GetAwayAsync(
        IReadOnlyCollection<long> userIds, DateTimeOffset now, CancellationToken ct = default)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0)
            return new HashSet<long>();

        var cutoff = UserPresence.AbsentIfLastActiveBy(now.UtcDateTime);

        // Past every filter, as the stamp middleware is: the dispatcher runs from jobs and requests alike, and
        // a request's user scope must not hide the other recipients' rows.
        var away = await db.Users
            .IgnoreQueryFilters()
            .Where(u => ids.Contains(u.Id) && u.LastActiveAt != null && u.LastActiveAt <= cutoff)
            .Select(u => u.Id)
            .ToListAsync(ct);

        return away.ToHashSet();
    }
}
