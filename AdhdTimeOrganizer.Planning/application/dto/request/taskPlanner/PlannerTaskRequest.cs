using AdhdTimeOrganizer.Planning.domain.model.@enum;
using AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner.template;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using Sydowwe.Framework.application.dto.request.@interface;

namespace AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner;

public record PlannerTaskRequest : BasePlannerTaskRequest, IMyRequest<PlannerTask>
{
    public required PlannerTaskStatus Status { get; init; }

    /// <summary>
    /// The day this task belongs to, when the caller has one. Optional now: send <see cref="Date"/> instead if
    /// you have a date rather than a calendar id. Exactly one of the two is required.
    /// </summary>
    public long? CalendarId { get; init; }

    /// <summary>
    /// The day this task belongs to, named by date — the calendar row is created if the user has none, which
    /// is the only way to plan a date past the seeded horizon. Ignored when <see cref="CalendarId"/> is set.
    /// <para>
    /// Resolution happens in the endpoint, not here: <see cref="ToEntity"/> is a property and cannot reach the
    /// database, so it leaves <c>CalendarId</c> at 0 for the endpoint to fill in. Anything constructing a task
    /// from this DTO outside <c>CreatePlannerTaskEndpoint</c> must do the same — see
    /// <c>ApplyTemplatePlannerTaskEndpoint</c>, which stamps the resolved id onto every task it builds.
    /// </para>
    /// </summary>
    public DateOnly? Date { get; init; }

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
        CalendarId = CalendarId ?? 0,
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
        // Left alone when the caller named its day by date instead — the endpoint has already resolved that
        // into CalendarId on the entity, and overwriting it with 0 here would orphan the task.
        if (CalendarId is { } calendarId)
            entity.CalendarId = calendarId;
        entity.TodolistItemId = TodolistId;
    }
}