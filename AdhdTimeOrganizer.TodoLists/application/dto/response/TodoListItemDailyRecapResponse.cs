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

    /// <summary>
    /// The item's id. The client keys an earned reward by item, because two items may share one leisure
    /// activity and each one's reward is dismissed or started on its own.
    /// </summary>
    public required long ItemId { get; init; }

    /// <summary>
    /// The leisure activity this item was paired with (temptation bundling), or null when it has none.
    /// A backlog profile's id is its activity id, so the client resolves the reward's name and duration
    /// itself. Every completed item is returned regardless — the client filters on this.
    /// </summary>
    public long? PairedLeisureActivityId { get; init; }

    /// <summary>
    /// When the item was completed, as a UTC instant. Never null — a recap row is completed by definition.
    /// </summary>
    public required DateTime CompletedTimestamp { get; init; }

    /// <summary>
    /// Whether the paired reward has been taken: a recording of <see cref="PairedLeisureActivityId"/>
    /// started at or after <see cref="CompletedTimestamp"/> and before the end of the recapped day.
    /// Derived, not recorded, so a reward still running (no recording yet) reads <c>false</c>, and one
    /// recording satisfies every item paired to that activity that was completed before it started.
    /// Always <c>false</c> when there is no pairing.
    /// </summary>
    public required bool RewardTaken { get; init; }
}

/// <summary>
/// The items this user completed on one day of their own calendar, in completion order, with the time
/// recorded against each.
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
