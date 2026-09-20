using AdhdTimeOrganizer.Core.domain.model.@enum;
using Sydowwe.Framework.application.dto.dto;

namespace AdhdTimeOrganizer.History.application.dto.response.calendar;

public record CalendarActivityDaySummary
{
    public long Id { get; init; }
    public DateOnly Date { get; init; }
    public DayType DayType { get; init; }
    public int DayIndex { get; init; }
    public string? Label { get; init; }

    /// <summary>Stable holiday key for the client to translate; <see cref="HolidayName"/> is the Slovak fallback.</summary>
    public string? HolidayKey { get; init; }
    public string? HolidayName { get; init; }
    public TimeDto WakeUpTime { get; init; } = null!;
    public TimeDto BedTime { get; init; } = null!;
    public long TotalSeconds { get; init; }
    public int SessionCount { get; init; }
    public List<CalendarTopRoleItem> TopRoles { get; init; } = [];
}

public record CalendarTopRoleItem
{
    /// <summary>The role this row aggregates. Never null — every activity has a required role.</summary>
    public long RoleId { get; init; }

    public string RoleName { get; init; } = null!;

    /// <summary>
    /// The role's colour, or <c>null</c> when it has none — passed through rather than flattened to
    /// <c>""</c>, so this reads like every other group-shaped response in the slice and a client can tell
    /// "no colour set" from "colour is the empty string".
    /// </summary>
    public string? Color { get; init; }
    public long TotalSeconds { get; init; }
}