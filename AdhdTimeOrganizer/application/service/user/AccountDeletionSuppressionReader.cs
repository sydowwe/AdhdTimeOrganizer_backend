using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.infrastructure.persistence;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Contracts.notification;

namespace AdhdTimeOrganizer.application.service.user;

/// <summary>
/// P3-07: a user whose account is scheduled for deletion (<see cref="User.DeletionScheduledFor"/>) gets nothing pushed or
/// mailed until it is either deleted or kept — every kind, whatever their preferences say. The background sweeps keep
/// computing for them, and the bell rows are still written, so a user who keeps the account finds what they missed.
/// <para>
/// Scheduling also deletes the user's push subscriptions, which on its own stops the pushes. This is what stops the
/// emails a user opted into, and any push to a subscription a web sign-in registers during the grace period.
/// </para>
/// <para>
/// Registered by hand in <c>ModuleServiceExtensions</c>: the dispatcher takes it as an <b>optional</b> dependency, so a
/// missing registration fails nowhere — the opted-in mails simply keep reaching a person who asked to be erased.
/// <c>ModuleWiringTests</c> pins the registration; <c>AccountDeletionGraceTests</c> pins the effect on what the fake
/// senders receive.
/// </para>
/// </summary>
public sealed class AccountDeletionSuppressionReader(AppDbContext db) : IRecipientSuppressionReader
{
    public async Task<IReadOnlySet<long>> GetSuppressedAsync(IReadOnlyCollection<long> userIds, CancellationToken ct = default)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0)
            return new HashSet<long>();

        // Past every filter, as the engagement reader is: the dispatcher runs from jobs and requests alike, and a
        // request's user scope must not hide the other recipients' rows.
        var suppressed = await db.Users
            .IgnoreQueryFilters()
            .Where(u => ids.Contains(u.Id) && u.DeletionScheduledFor != null)
            .Select(u => u.Id)
            .ToListAsync(ct);

        return suppressed.ToHashSet();
    }
}
