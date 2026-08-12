using AdhdTimeOrganizer.Core.application.dto.request.activity;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using Sydowwe.Framework.application.dto.request.@interface;
using Sydowwe.Framework.domain.valueObject;

namespace AdhdTimeOrganizer.History.application.dto.request.history;

public record ActivityHistoryRequest : ActivityIdRequest, IMyRequest<ActivityHistory>
{
    public required DateTime StartTimestamp { get; init; }


    public required IntTime Length { get; init; }

    /// <summary>
    /// The to-do item this recording was raised from, when the save-to-history prompt fired on
    /// completing one. Null for a recording entered directly against an activity.
    /// </summary>
    /// <remarks>
    /// A step's prompt sends the <b>parent item's</b> id — see <see cref="ActivityHistory.TodoListItemId"/>.
    /// This is what makes the daily recap's per-item time exact rather than inferred from the
    /// activity, so a prompt that drops it silently reports the task as taking zero seconds.
    /// </remarks>
    public long? TodoListItemId { get; init; }

    /// <summary>Same as <see cref="TodoListItemId"/>, for a routine to-do item.</summary>
    public long? RoutineTodoListId { get; init; }

    public ActivityHistory ToEntity => new()
    {
        UserId = 0,
        ActivityId = ActivityId,
        StartTimestamp = StartTimestamp,
        Length = Length,
        EndTimestamp = StartTimestamp.AddSeconds(Length.TotalSeconds),
        TodoListItemId = TodoListItemId,
        RoutineTodoListId = RoutineTodoListId
    };

    public void UpdateEntity(ActivityHistory e)
    {
        e.ActivityId = ActivityId;
        e.StartTimestamp = StartTimestamp;
        e.Length = Length;

        // The two item links are deliberately NOT written here. They record which prompt raised the
        // recording, which the edit form neither shows nor knows about — assigning them would post
        // null on every ordinary edit and silently unlink the row from its task, dropping it out of
        // that day's recap. Editing a row's duration is not a statement about what raised it.
    }
}
