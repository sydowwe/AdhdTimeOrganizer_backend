namespace AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner;

/// <summary>
/// Copy one day's plan onto several other days.
/// <para>
/// The source day's tasks are <b>not</b> sent: the server re-reads them, which is strictly less work than the
/// client fetching them and re-serialising each one as a create request per target day, and it removes the
/// window in which the client could copy a stale version of the source day.
/// </para>
/// </summary>
public record CopyPlannerTasksToDaysRequest
{
    /// <summary>The day being copied from.</summary>
    public required long SourceCalendarId { get; init; }

    /// <summary>
    /// The days being copied to. Distinct, and never containing <see cref="SourceCalendarId"/> — see
    /// <c>CopyPlannerTasksToDaysValidator</c>.
    /// </summary>
    public required List<long> TargetCalendarIds { get; init; }
}
