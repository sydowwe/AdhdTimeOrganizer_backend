using AdhdTimeOrganizer.application.dto.request.taskPlanner.template;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Core.domain.model.@enum;
using Sydowwe.Framework.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.request.taskPlanner;

public record PlannerTaskRequest : BasePlannerTaskRequest, IMyRequest<PlannerTask>
{
    public required PlannerTaskStatus Status { get; init; }

    public required long CalendarId { get; init; }

    public long? TodolistId { get; init; }

    public PlannerTask ToEntity => new()
    {
        UserId = 0,
        StartTime = StartTime.ToTimeOnly(),
        EndTime = EndTime.ToTimeOnly(),
        IsBackground = IsBackground,
        Location = Location,
        Notes = Notes,
        ActivityId = ActivityId,
        ImportanceId = ImportanceId,
        Status = Status,
        CalendarId = CalendarId,
        TodolistItemId = TodolistId
    };

    public void UpdateEntity(PlannerTask entity)
    {
        entity.StartTime = StartTime.ToTimeOnly();
        entity.EndTime = EndTime.ToTimeOnly();
        entity.IsBackground = IsBackground;
        entity.Location = Location;
        entity.Notes = Notes;
        entity.ActivityId = ActivityId;
        entity.ImportanceId = ImportanceId;
        entity.Status = Status;
        entity.CalendarId = CalendarId;
        entity.TodolistItemId = TodolistId;
    }
}