using FastEndpoints;

namespace AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner;

/// <summary>Query of <c>GET /calendar/day-plan/{date}/next-free-slot</c> (FO4).</summary>
public record NextFreeSlotRequest
{
    /// <summary>The length of the block to place, in minutes (1–1439).</summary>
    [QueryParam]
    public int Minutes { get; set; }

    /// <summary>
    /// Optional: the slot may start no earlier than this, in minutes past midnight on the user's clock (0–1439).
    /// <i>Move later</i> on an upcoming block sends the block's own start, so the answer is never earlier than it.
    /// </summary>
    [QueryParam]
    public int? NotBeforeMinute { get; set; }

    /// <summary>
    /// Optional: a task on the day to leave out of the occupied time — the block being moved, which must not stand
    /// in its own way.
    /// </summary>
    [QueryParam]
    public long? ExcludeTaskId { get; set; }
}
