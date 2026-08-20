using AdhdTimeOrganizer.Core.domain.model.@enum;
using Sydowwe.Framework.application.dto.dto;

namespace AdhdTimeOrganizer.application.dto.response.activityHistory.dashboard.calendar;

public record CalendarActivityDaySummary
{
    public long Id { get; init; }
    public DateOnly Date { get; init; }
    public DayType DayType { get; init; }
    public int DayIndex { get; init; }
    public string? Label { get; init; }
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
    public string Color { get; init; } = null!;
    public long TotalSeconds { get; init; }
}