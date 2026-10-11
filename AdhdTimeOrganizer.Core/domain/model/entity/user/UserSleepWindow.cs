using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.Core.domain.model.entity.user;

/// <summary>
/// FO10: when a user's day starts and ends — what a calendar day's <c>WakeUpTime</c> / <c>BedTime</c> starts as
/// before anyone edits it. Owned by <see cref="User"/> (<see cref="User.DefaultWakeUpTime"/>,
/// <see cref="User.DefaultBedTime"/>) because more than one slice reads it: Planning stamps it onto every day row it
/// creates and packs a re-plan up to its bed time, and Routines sends the morning digest at its wake-up.
/// <para>
/// A bed time at or before the wake-up time means "after midnight" (00:00 is the default, and 01:30 is a late
/// night, not a nap); every reader of the window already treats it that way (<c>DayPacking.WindowEnd</c>).
/// </para>
/// </summary>
public readonly record struct UserSleepWindow(TimeOnly WakeUpTime, TimeOnly BedTime)
{
    /// <summary>
    /// The window a user starts with, and the one used for a user id that has no row. 08:00 to midnight is what
    /// every calendar day was created with before the preference existed, so a user who never sets it sees no change.
    /// </summary>
    public static readonly UserSleepWindow Fallback = new(new TimeOnly(8, 0), new TimeOnly(0, 0));

    /// <summary>
    /// The user's own window. Scoped by id with the query filters lifted, like <c>UserTimeZoneResolver</c>: the
    /// callers include unauthenticated sweeps and seeders acting for a user other than the signed-in one.
    /// </summary>
    public static async Task<UserSleepWindow> ForUserAsync(DbContext db, long userId, CancellationToken ct)
    {
        var window = await db.Set<User>()
            .IgnoreQueryFilters()
            .Where(u => u.Id == userId)
            .Select(u => new { u.DefaultWakeUpTime, u.DefaultBedTime })
            .FirstOrDefaultAsync(ct);

        return window is null ? Fallback : new UserSleepWindow(window.DefaultWakeUpTime, window.DefaultBedTime);
    }
}
