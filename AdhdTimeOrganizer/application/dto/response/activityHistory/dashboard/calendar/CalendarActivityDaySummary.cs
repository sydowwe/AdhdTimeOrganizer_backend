using AdhdTimeOrganizer.application.dto.dto;
using AdhdTimeOrganizer.domain.model.@enum;

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
    public string RoleName { get; init; } = null!;
    public string Color { get; init; } = null!;
    public long TotalSeconds { get; init; }
}
