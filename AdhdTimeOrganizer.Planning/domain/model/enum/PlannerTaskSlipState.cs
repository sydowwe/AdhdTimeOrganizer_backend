namespace AdhdTimeOrganizer.Planning.domain.model.@enum;

/// <summary>
/// Where a not-started foreground block stands against the user's clock (FO4). Decided by
/// <c>PlannerTaskMissedRule.Classify</c> and nowhere else; derived, never stored.
/// <para>
/// On the wire as the member name (<c>"Upcoming"</c> / <c>"Late"</c> / <c>"Missed"</c>), through the host's bare
/// <c>JsonStringEnumConverter</c>.
/// </para>
/// </summary>
public enum PlannerTaskSlipState
{
    /// <summary>Its start has not come yet.</summary>
    Upcoming,

    /// <summary>Its start has passed and its end has not: the slot is running without the user.</summary>
    Late,

    /// <summary>Its end has passed and it never started.</summary>
    Missed
}
