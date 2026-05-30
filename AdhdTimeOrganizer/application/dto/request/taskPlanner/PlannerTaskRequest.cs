using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.dto.request.@interface;
using AdhdTimeOrganizer.application.dto.request.taskPlanner.template;
using AdhdTimeOrganizer.domain.model.@enum;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;

namespace AdhdTimeOrganizer.application.dto.request.taskPlanner;

public record PlannerTaskRequest : BasePlannerTaskRequest, IMyRequest<PlannerTask>
{
    [Required]
    public required PlannerTaskStatus Status { get; init; }
    [Required]
    public required long CalendarId { get; init; }

    public long? TodolistId { get; init; }

    public PlannerTask ToEntity => new()
    {
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