using AdhdTimeOrganizer.Core.domain.model.entity.user;

namespace AdhdTimeOrganizer.Planning.domain.serviceContract;

/// <summary>
/// FO10: carries a changed <see cref="UserSleepWindow"/> onto the days that inherit it. <c>CalendarSeeder</c> fills two
/// years of rows ahead, each stamped with the window of the moment it was created, so without this a new default would
/// reach only days created after it — about two years out.
/// </summary>
public interface ICalendarSleepWindowSync
{
    /// <summary>
    /// Rewrites the sleep window of the user's days from their today (in their zone) onwards to
    /// <see cref="User.SleepWindow"/> — except a day whose window was chosen for it
    /// (<c>Calendar.HasCustomSleepWindow</c>). Past days are history and are never touched. Returns how many days
    /// changed. A single set-based update: it commits on its own and needs no <c>SaveChanges</c>.
    /// </summary>
    Task<int> SyncFutureDaysAsync(User user, CancellationToken ct);
}
