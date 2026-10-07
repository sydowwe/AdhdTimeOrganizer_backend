namespace AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner;

/// <summary>
/// <c>POST /calendar/day-plan/{date}/replan/apply</c> — which tasks the apply changed (FO4). The client re-reads
/// <c>GET /calendar/day-plan/{date}</c> for the day itself. No count of postponements is ever sent.
/// </summary>
public record ApplyDayReplanResponse
{
    /// <summary>Tasks moved within today.</summary>
    public required IReadOnlyList<long> MovedTaskIds { get; init; }

    /// <summary>Tasks moved to tomorrow, same time of day.</summary>
    public required IReadOnlyList<long> MovedToTomorrowTaskIds { get; init; }

    /// <summary>Tasks skipped with the reason <c>noTime</c>.</summary>
    public required IReadOnlyList<long> SkippedTaskIds { get; init; }
}
