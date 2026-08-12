namespace AdhdTimeOrganizer.TodoLists.application.dto.response.todoList;

/// <summary>
/// One completed item in the recap. <see cref="Name"/> is the item's display name, which for a to-do
/// item is its activity's name — the item carries no name of its own.
/// </summary>
public record DailyRecapItem
{
    public required string Name { get; init; }

    /// <summary>
    /// Seconds recorded against this specific item on the recapped day — not an activity-wide total.
    /// Zero when the item was completed without saving a recording, and for anything completed before
    /// recordings carried an item link.
    /// </summary>
    public required long TimeLoggedSeconds { get; init; }
}

/// <summary>
/// The items this user completed on one day, with the time recorded against each.
/// </summary>
public record TodoListItemDailyRecapResponse
{
    /// <summary>Never null; an empty array on a day with no completions.</summary>
    public required List<DailyRecapItem> Items { get; init; }

    /// <summary>
    /// Sum of <see cref="DailyRecapItem.TimeLoggedSeconds"/> across <see cref="Items"/>. Reconciles
    /// with the visible rows by construction — the per-item figures come from an exact item link
    /// rather than from activity matching, so the same seconds cannot land on two rows.
    /// </summary>
    public required long TotalTimeLoggedSeconds { get; init; }
}
