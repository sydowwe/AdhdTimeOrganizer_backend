using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.dto.request.@interface;
using AdhdTimeOrganizer.application.dto.request.taskPlanner.template;
using AdhdTimeOrganizer.domain.model.@enum;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;

namespace AdhdTimeOrganizer.application.dto.request.taskPlanner;

public record RepeatingPlannerTaskRequest : BasePlannerTaskRequest, IMyRequest<RepeatingPlannerTask>
{
    [Required]
    public required bool IsActive { get; init; }

    [Required]
    public required RecurrenceType RecurrenceType { get; init; }

    public IEnumerable<string> ScheduledDays { get; init; } = [];
    public IEnumerable<int> ScheduledDates { get; init; } = [];
    public DateOnly? ActiveFromDate { get; init; }
    public DateOnly? ActiveToDate { get; init; }
    public IEnumerable<string> ScheduledForDayTypes { get; init; } = [];

    public RepeatingPlannerTask ToEntity => new()
    {
        StartTime = StartTime.ToTimeOnly(),
        EndTime = EndTime.ToTimeOnly(),
        IsBackground = IsBackground,
        Location = Location,
        Notes = Notes,
        ActivityId = ActivityId,
        ImportanceId = ImportanceId,
        IsActive = IsActive,
        RecurrenceType = RecurrenceType,
        ScheduledDays = ScheduledDays,
        ScheduledDates = ScheduledDates,
        ActiveFromDate = ActiveFromDate,
        ActiveToDate = ActiveToDate,
        ScheduledForDayTypes = ScheduledForDayTypes
    };

    public void UpdateEntity(RepeatingPlannerTask entity)
    {
        entity.StartTime = StartTime.ToTimeOnly();
        entity.EndTime = EndTime.ToTimeOnly();
        entity.IsBackground = IsBackground;
        entity.Location = Location;
        entity.Notes = Notes;
        entity.ActivityId = ActivityId;
        entity.ImportanceId = ImportanceId;
        entity.IsActive = IsActive;
        entity.RecurrenceType = RecurrenceType;
        entity.ScheduledDays = ScheduledDays;
        entity.ScheduledDates = ScheduledDates;
        entity.ActiveFromDate = ActiveFromDate;
        entity.ActiveToDate = ActiveToDate;
        entity.ScheduledForDayTypes = ScheduledForDayTypes;
    }
}
