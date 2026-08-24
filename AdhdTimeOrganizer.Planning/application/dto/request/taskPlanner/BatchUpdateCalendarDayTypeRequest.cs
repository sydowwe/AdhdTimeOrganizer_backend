using AdhdTimeOrganizer.Core.domain.model.@enum;

namespace AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner;

/// <summary>
/// Set the day type on several days at once.
/// <para>
/// <b>Only</b> the day type, unlike <see cref="CalendarRequest"/>. The bulk action it replaces cloned the
/// whole calendar request from what <c>calendar/filter</c> had returned and overwrote one field, so every
/// other column was echoed back unchanged; a batch that carried them would be able to change things the
/// per-day path never could — and would need the client to hold a full, current copy of every selected day.
/// </para>
/// </summary>
public record BatchUpdateCalendarDayTypeRequest
{
    /// <summary>The days to change. Distinct — see <c>BatchUpdateCalendarDayTypeValidator</c>.</summary>
    public required List<long> CalendarIds { get; init; }

    public required DayType DayType { get; init; }
}
