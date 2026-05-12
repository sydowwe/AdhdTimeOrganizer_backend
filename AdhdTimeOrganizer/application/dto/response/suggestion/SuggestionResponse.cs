using AdhdTimeOrganizer.application.dto.dto;
using AdhdTimeOrganizer.application.dto.response.activity;
using AdhdTimeOrganizer.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.domain.model.@enum;

namespace AdhdTimeOrganizer.application.dto.response.suggestion;

public record SuggestionResponse
{
    public long? RepeatingPlannerTaskId { get; init; }
    public required ActivityResponse Activity { get; init; }
    public TaskImportanceResponse? Importance { get; init; }
    public required TimeDto StartTime { get; init; }
    public required TimeDto EndTime { get; init; }
    public bool IsBackground { get; init; }
    public string? Location { get; init; }
    public string? Notes { get; init; }
    public required string Color { get; init; }
    public required RecurrenceType RecurrenceType { get; init; }
    public required string[] ScheduledDays { get; init; }
    public required int[] ScheduledDates { get; init; }
    public DateOnly? ActiveFromDate { get; init; }
    public DateOnly? ActiveToDate { get; init; }
    public required string[] ScheduledForDayTypes { get; init; }
    public required SuggestionSourceType SourceType { get; init; }
    public int? OccurrenceCount { get; init; }
}
