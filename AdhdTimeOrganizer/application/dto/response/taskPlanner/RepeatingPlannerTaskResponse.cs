using AdhdTimeOrganizer.application.dto.dto;
using AdhdTimeOrganizer.application.dto.response.activity;
using AdhdTimeOrganizer.application.dto.response.@base;
using AdhdTimeOrganizer.domain.model.@enum;

namespace AdhdTimeOrganizer.application.dto.response.taskPlanner;

public record RepeatingPlannerTaskResponse : IdResponse
{
    public required ActivityResponse Activity { get; init; }
    public required TaskImportanceResponse Importance { get; init; }
    public required TimeDto StartTime { get; init; }
    public required TimeDto EndTime { get; init; }
    public required bool IsBackground { get; init; }
    public string? Location { get; init; }
    public string? Notes { get; init; }
    public required string Color { get; init; }
    public required bool IsActive { get; init; }
    public required RecurrenceType RecurrenceType { get; init; }
    public required string[] ScheduledDays { get; init; }
    public required int[] ScheduledDates { get; init; }
    public DateOnly? ActiveFromDate { get; init; }
    public DateOnly? ActiveToDate { get; init; }
    public required string[] ScheduledForDayTypes { get; init; }
}
