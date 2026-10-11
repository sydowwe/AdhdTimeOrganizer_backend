using AdhdTimeOrganizer.Core.application.seam;

namespace AdhdTimeOrganizer.Core.application.@event;

/// <summary>
/// Raised after an activity's archived flag has actually changed and committed. Archiving drops the rows that hang
/// off an activity out of every read that joins it under <c>AppQueryFilters.ActivityArchived</c> — its to-do items
/// among them — and restoring brings them back, so whatever a slice scheduled from those rows has to follow.
/// Published by Core's <c>SetArchivedActivityEndpoint</c>; handled by TodoLists, which withdraws the due reminders
/// of the activity's to-dos on archive and re-registers them on restore.
/// </summary>
/// <param name="UserId">
/// The activity's owner. The handler runs in its own scope, where the per-user filter cannot be relied on, so it
/// scopes on this rather than on the ambient one — same reason as <see cref="TodoListItemIsDoneChangedEvent"/>.
/// </param>
public record ActivityArchivedChangedEvent(long ActivityId, long UserId, bool IsArchived) : ISeamEvent;
