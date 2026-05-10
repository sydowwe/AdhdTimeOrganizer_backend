using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.dto.request.@interface;
using AdhdTimeOrganizer.application.dto.request.taskPlanner.template;
using AdhdTimeOrganizer.domain.model.@enum;

namespace AdhdTimeOrganizer.application.dto.request.taskPlanner;

public record RepeatingPlannerTaskRequest : BasePlannerTaskRequest, IMyRequest
{
    [Required]
    public required bool IsActive { get; init; }

    [Required]
    public required RecurrenceType RecurrenceType { get; init; }

    public string[] ScheduledDays { get; init; } = [];
    public int[] ScheduledDates { get; init; } = [];
    public DateOnly? ActiveFromDate { get; init; }
    public DateOnly? ActiveToDate { get; init; }
    public string[] ScheduledForDayTypes { get; init; } = [];
}
