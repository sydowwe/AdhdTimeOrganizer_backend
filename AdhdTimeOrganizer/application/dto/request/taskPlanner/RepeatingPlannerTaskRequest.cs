using AdhdTimeOrganizer.application.dto.request.taskPlanner.template;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Core.domain.model.@enum;
using Sydowwe.Framework.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.request.taskPlanner;

public record RepeatingPlannerTaskRequest : BasePlannerTaskRequest, IMyRequest<RepeatingPlannerTask>
{
    public required bool IsActive { get; init; }


    public required RecurrenceType RecurrenceType { get; init; }

    public IEnumerable<string> ScheduledDays { get; init; } = [];
    public IEnumerable<int> ScheduledDates { get; init; } = [];
    public DateOnly? ActiveFromDate { get; init; }
    public DateOnly? ActiveToDate { get; init; }
    public IEnumerable<string> ScheduledForDayTypes { get; init; } = [];

    public RepeatingPlannerTask ToEntity => new()
    {
        UserId = 0,
        StartTime = StartTime.ToTimeOnly(),
        EndTime = EndTime.ToTimeOnly(),
        IsBackground = IsBackground,
        Location = Location,
        Notes = Notes,
        ActivityId = ActivityId,
        ImportanceId = ImportanceId,
        IsActive = IsActive,
        RecurrenceType = RecurrenceType,
        ScheduledDays = ScheduledDays.ToList(),
        ScheduledDates = ScheduledDates.ToList(),
        ActiveFromDate = ActiveFromDate,
        ActiveToDate = ActiveToDate,
        ScheduledForDayTypes = ScheduledForDayTypes.ToList()
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
        entity.ScheduledDays = ScheduledDays.ToList();
        entity.ScheduledDates = ScheduledDates.ToList();
        entity.ActiveFromDate = ActiveFromDate;
        entity.ActiveToDate = ActiveToDate;
        entity.ScheduledForDayTypes = ScheduledForDayTypes.ToList();
    }
}