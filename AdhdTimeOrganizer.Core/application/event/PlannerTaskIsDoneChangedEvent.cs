using AdhdTimeOrganizer.Core.application.seam;

namespace AdhdTimeOrganizer.Core.application.@event;

/// <summary>
/// Raised after a planner task's done state has been committed, so the to-do item and routine item
/// linked to the same activity can follow it.
/// </summary>
/// <remarks>
/// Published by Planning's <c>PatchPlannerTaskStatusEndpoint</c> and by the host's
/// <c>ActivityTimeRecordedEventHandler</c>; handled host-side, because the single handler writes into
/// <b>both</b> TodoLists and Routines in one <c>SaveChanges</c>.
/// </remarks>
public record PlannerTaskIsDoneChangedEvent(long ActivityId, long UserId, bool NewIsDone, long? TodoListItemId) : ISeamEvent;
