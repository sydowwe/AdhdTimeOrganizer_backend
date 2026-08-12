namespace AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;

public class TodoListItem : BaseTodoListItem
{
    public required long TaskPriorityId { get; set; }
    public TaskPriority TaskPriority { get; set; } = null!;

    public DateOnly? DueDate { get; set; }
    public TimeOnly? DueTime { get; set; }

    public long? TodoListId { get; set; }
    public TodoList? TodoList { get; set; }

    /// <summary>
    /// Temptation bundling — the leisure activity this task is paired with, or null for no pairing.
    /// An Activity id, not an ActivityBacklogProfile id: the picker is populated from
    /// the backlog profiles, but what is stored is the activity they are keyed by.
    /// Navigation-free on purpose — reads carry only the id, and this slice cannot see
    /// AdhdTimeOrganizer.ActivityProfiles to resolve anything more.
    /// </summary>
    public long? PairedLeisureActivityId { get; set; }

    /// <summary>
    /// When the item last transitioned to done, or null while it is not done. This is what makes
    /// "completed today" answerable — <c>IsDone</c> alone cannot distinguish a task finished an hour
    /// ago from one finished last March.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Do not set this by hand.</b> <c>TodoListItemCompletionInterceptor</c> stamps it from the
    /// ChangeTracker on every save where <c>IsDone</c> actually flips, and clears it when the item is
    /// un-done. There are five-odd places that write <c>IsDone</c> — the two toggle bases, the step
    /// toggle, the update request and the host's <c>ActivityTimeRecordedEventHandler</c> — and a site
    /// that forgot to stamp would not fail to build or throw; the item would simply stop appearing in
    /// its day's recap. Centralising it is what makes that unmissable.
    /// <para>
    /// Deliberately not on <c>BaseTodoListItem</c>: routine items recur, and their per-period
    /// completion is already recorded by <c>RoutinePeriodCompletion</c>. A second, last-write-wins
    /// timestamp there would be a worse answer to a question that is already answered.
    /// </para>
    /// </remarks>
    public DateTime? CompletedTimestamp { get; set; }
}