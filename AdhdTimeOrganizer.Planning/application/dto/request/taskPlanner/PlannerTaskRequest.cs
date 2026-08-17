using AdhdTimeOrganizer.Planning.domain.model.@enum;
using AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner.template;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using Sydowwe.Framework.application.dto.dto;
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

    /// <summary>
    /// The minute work actually began, for a task that is created already under way — "I'm doing this now",
    /// which is how the leisure picker commits a suggestion. Without it that flow had to POST the task and then
    /// PATCH its status, and a create that named <c>InProgress</c> produced a task the planner and the home
    /// now-bar could not show as started, since they read this column rather than the status alone.
    /// <para>
    /// Only meaningful for <c>InProgress</c> / <c>Completed</c>: <c>PlannerTask.ApplyStatus</c> clears the actual
    /// times for the other two, so <c>PlannerTaskValidator</c> rejects the combination rather than accepting a
    /// value the next status change would silently drop.
    /// </para>
    /// <para>
    /// <b>Create-only: <see cref="UpdateEntity"/> deliberately leaves the column alone.</b> A full PUT that
    /// omitted this field would otherwise wipe the start time of a task already under way — which is every
    /// existing update caller, since the day-planner dialog and the drag-to-move path do not send it.
    /// Changing the time afterwards is what <c>PATCH /planner-task/{id}/status</c> is for.
    /// </para>
    /// </summary>
    public TimeDto? ActualStartTime { get; init; }

    public PlannerTask ToEntity => new()
    {
        ActualStartTime = ActualStartTime?.ToTimeOnly(),
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