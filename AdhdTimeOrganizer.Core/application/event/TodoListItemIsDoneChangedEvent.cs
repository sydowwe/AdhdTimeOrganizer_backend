using AdhdTimeOrganizer.Core.application.seam;

namespace AdhdTimeOrganizer.Core.application.@event;

/// <summary>
/// Raised after a to-do item's done state has been committed, so today's planner tasks linked to it can
/// follow it and their reminders can be resynced. Published by TodoLists; the handler touches Planning
/// only.
/// </summary>
public record TodoListItemIsDoneChangedEvent(long TodoListItemId, bool NewIsDone) : ISeamEvent;
