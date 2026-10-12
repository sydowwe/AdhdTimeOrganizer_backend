namespace AdhdTimeOrganizer.TodoLists.application.dto.response.todoList;

/// <summary>
/// How long one group's completed items took against their estimates. Only groups with enough items appear at all
/// (<c>EstimateFitRule.TodoItemGroupMinimumSamples</c>); a smaller group is absent, not present with a weak figure.
/// </summary>
public record EstimateFitGroup
{
    /// <summary>
    /// The list, category or priority id, per the request's <c>groupBy</c>. <c>null</c> for items on no list, or on
    /// an uncategorised list.
    /// </summary>
    public required long? Key { get; init; }

    /// <summary>
    /// The median of each item's logged time divided by its estimate. 1.5 means the group's items took about one and
    /// a half times what was estimated.
    /// </summary>
    public required double LoggedRatio { get; init; }

    /// <summary>
    /// The items behind <see cref="LoggedRatio"/>: completed in the window, with an estimate and some time logged
    /// against the item itself.
    /// </summary>
    public required int SampleCount { get; init; }

    /// <summary>
    /// The median of each item's first planner block divided by its estimate, over the items above that were ever
    /// planned. <c>null</c> when fewer of them than the threshold were.
    /// </summary>
    public required double? PlannedRatio { get; init; }

    /// <summary>How many of the <see cref="SampleCount"/> items had a planner block.</summary>
    public required int PlannedSampleCount { get; init; }
}

/// <summary>Time logged against one item, across every day.</summary>
public record EstimateFitItemLoggedTime
{
    public required long ItemId { get; init; }
    public required long LoggedSeconds { get; init; }
}

/// <summary>
/// F3-01: per group, how the user's to-do estimates compared with the time logged against the items and with the
/// block they were planned into; and per item, the time logged so far.
/// </summary>
public record TodoListItemEstimateFitResponse
{
    /// <summary>Never null; empty while no group has enough items.</summary>
    public required List<EstimateFitGroup> Groups { get; init; }

    /// <summary>
    /// Every item with an estimate and logged time, open or completed in the window. Open items are here so a long
    /// item shows its progress; they never count towards a group, because their figure is not final.
    /// </summary>
    public required List<EstimateFitItemLoggedTime> Items { get; init; }
}
