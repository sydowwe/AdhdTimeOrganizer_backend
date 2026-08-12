using AdhdTimeOrganizer.Core.application.seam;

namespace AdhdTimeOrganizer.Core.application.@event;

/// <summary>
/// Raised after a routine to-do list's done state has been committed, so today's planner tasks for the
/// same activity can follow it. Published by Routines; the handler touches Planning only.
/// </summary>
public record RoutineTodoListIsDoneChangedEvent(long ActivityId, long UserId, bool NewIsDone) : ISeamEvent;
