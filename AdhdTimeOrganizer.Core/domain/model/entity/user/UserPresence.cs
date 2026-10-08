namespace AdhdTimeOrganizer.Core.domain.model.entity.user;

/// <summary>
/// FO5: the one rule for "is this user away from the app?", read off <see cref="User.LastActiveAt"/>. The thresholds
/// are the owner's (2026-10-06): the card greets after <see cref="GreetAfter"/> away, and the app goes quiet after
/// <see cref="QuietAfter"/>. So a three-day absence goes quiet but gets no card on return, which is deliberate: a long
/// weekend should not trigger a greeting.
/// <para>
/// This is <b>unmarked</b> absence, which is not FO1's away day. A day the user marked <c>Vacation</c> or
/// <c>SickDay</c> is away whether or not they opened the app on it (<c>DayTypeExtensions.IsAway</c>); a user who
/// simply stopped opening it is absent here and nowhere else. The welcome-back card reads both: this decides whether
/// it appears, and the marked days decide its copy.
/// </para>
/// </summary>
public static class UserPresence
{
    /// <summary>How often <see cref="User.LastActiveAt"/> is written at most. Far below either threshold, so the
    /// coarse stamp costs them no precision that matters.</summary>
    public static readonly TimeSpan StampInterval = TimeSpan.FromHours(1);

    /// <summary>An absence at least this long earns the welcome-back card on return.</summary>
    public static readonly TimeSpan GreetAfter = TimeSpan.FromDays(4);

    /// <summary>
    /// An absence at least this long makes the user <see cref="IsAbsent">absent</see>: non-essential notifications
    /// are held from here on (the back-off, FO5's second step, which is not built yet).
    /// </summary>
    public static readonly TimeSpan QuietAfter = TimeSpan.FromDays(3);

    /// <summary>How long after the return the card is still offered. Past it, a card nobody answered lapses: by then
    /// the user has been back long enough that "while you were away" is no longer the moment.</summary>
    public static readonly TimeSpan CardLifetime = TimeSpan.FromDays(2);

    /// <summary>
    /// Whether a user last active at <paramref name="lastActiveAt"/> is away at <paramref name="utcNow"/>. A user
    /// never measured (null) is not: the column only accumulates from the deploy that added it.
    /// </summary>
    public static bool IsAbsent(DateTime? lastActiveAt, DateTime utcNow) =>
        lastActiveAt is { } last && utcNow - last >= QuietAfter;

    /// <summary>Whether a card for a return at <paramref name="returnedAt"/> is still offered at <paramref name="utcNow"/>.</summary>
    public static bool CardIsOpen(DateTime? returnedAt, DateTime utcNow) =>
        returnedAt is { } back && utcNow - back < CardLifetime;
}
