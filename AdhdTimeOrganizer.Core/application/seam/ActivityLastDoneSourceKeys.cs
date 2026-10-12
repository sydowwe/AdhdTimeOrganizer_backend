namespace AdhdTimeOrganizer.Core.application.seam;

/// <summary>
/// The known <see cref="IActivityLastDoneSource.Key"/> values — one per slice holding positive evidence that an
/// activity happened. A key here with no implementation fails <c>SeamWiringTests</c>, not production.
/// </summary>
public static class ActivityLastDoneSourceKeys
{
    /// <summary>History's <c>ActivityHistory</c>: the latest recording's start, timer, manual or tracker alike.</summary>
    public const string ActivityHistory = "activityHistory";

    /// <summary>Planning's <c>PlannerTask</c>: the latest completed block, and the untouched past ones.</summary>
    public const string PlannerTask = "plannerTask";

    /// <summary>ActivityProfiles' <c>MemoryAnchor</c>: the latest anchored month.</summary>
    public const string MemoryAnchor = "memoryAnchor";
}
