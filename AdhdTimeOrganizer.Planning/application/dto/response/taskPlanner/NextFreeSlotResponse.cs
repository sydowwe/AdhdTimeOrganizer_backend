using Sydowwe.Framework.application.dto.dto;

namespace AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner;

/// <summary>
/// <c>GET /calendar/day-plan/{date}/next-free-slot</c> — the earliest stretch of the day long enough for one block
/// (FO4). <b>No room is a 200</b> with <see cref="Found"/> false and no times, never a 404.
/// </summary>
public record NextFreeSlotResponse
{
    public required DateOnly Date { get; init; }

    /// <summary>The length asked for, echoed.</summary>
    public required int Minutes { get; init; }

    public required bool Found { get; init; }

    /// <summary>The slot's start, or null when nothing fits.</summary>
    public TimeDto? StartTime { get; init; }

    /// <summary>The slot's end (start + minutes), or null when nothing fits.</summary>
    public TimeDto? EndTime { get; init; }
}
