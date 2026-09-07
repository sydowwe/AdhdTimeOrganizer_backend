using AdhdTimeOrganizer.Core.application.seam;

namespace AdhdTimeOrganizer.Core.application.@event;

/// <summary>
/// Raised after a to-do item's done state has been committed, so today's planner tasks linked to it can
/// follow it and their reminders can be resynced. Published by TodoLists; the handler touches Planning
/// only.
/// </summary>
/// <param name="UserId">
/// The item's owner. Carried for the same reason <see cref="RoutineTodoListIsDoneChangedEvent"/> carries
/// it: the handler runs in its own scope, and a publish from anywhere without an authenticated principal
/// leaves the global user filter a no-op — so the handler scopes on this rather than on the ambient one.
/// </param>
public record TodoListItemIsDoneChangedEvent(long TodoListItemId, long UserId, bool NewIsDone) : ISeamEvent;
