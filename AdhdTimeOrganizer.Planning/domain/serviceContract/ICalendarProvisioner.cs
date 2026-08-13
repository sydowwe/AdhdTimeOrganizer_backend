using AdhdTimeOrganizer.Planning.domain.model.entity;

namespace AdhdTimeOrganizer.Planning.domain.serviceContract;

/// <summary>
/// Resolves the <see cref="Calendar"/> row for a date, creating it if the user has none. The single entry
/// point for lazy day creation — every write path that can be the first thing to land on a date goes through
/// it rather than 404ing or inventing its own row.
/// <para>
/// Why it exists: <c>CalendarSeeder</c> fills whole years at user setup and there is no create-calendar
/// endpoint, so a date past the seeded horizon used to be unplannable — the planner would render an empty day
/// that no write could attach to, with nothing in the app able to make the missing row.
/// </para>
/// </summary>
public interface ICalendarProvisioner
{
    /// <summary>
    /// The user's calendar row for <paramref name="date"/>, created and <b>saved</b> if it did not exist.
    /// <para>
    /// It commits, so call it before staging writes of your own — anything already pending on the ambient
    /// <c>DbContext</c> is committed along with it.
    /// </para>
    /// </summary>
    Task<Calendar> EnsureForDateAsync(long userId, DateOnly date, CancellationToken ct = default);
}
