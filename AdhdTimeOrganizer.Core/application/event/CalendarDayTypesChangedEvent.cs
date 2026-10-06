using AdhdTimeOrganizer.Core.application.seam;

namespace AdhdTimeOrganizer.Core.application.@event;

/// <summary>
/// Raised after the day type of one or more of a user's calendar days has been committed with a different
/// value. Published by Planning (the single-day update and the batch day-type endpoint); handled by Routines,
/// which excuses closed cycles that are now mostly away — the user typically marks a sick week only once they
/// are back, after the cycles it covered have already been scored.
/// <para>
/// Carries the dates rather than the new types: a handler re-reads the days through
/// <see cref="ICalendarDayLookup"/>, so two events racing each other cannot leave it acting on a stale value.
/// </para>
/// </summary>
public record CalendarDayTypesChangedEvent(long UserId, IReadOnlyList<DateOnly> Dates) : ISeamEvent;
