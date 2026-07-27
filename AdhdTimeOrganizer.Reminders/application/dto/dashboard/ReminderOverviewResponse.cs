namespace AdhdTimeOrganizer.Reminders.application.dto.dashboard;

/// <summary>
/// The reminders overview (phase 05a) — a single roll-up of the registry + dispatch log for the admin landing
/// view: upcoming counts by owner module / kind / next-7-days, recent dispatch outcomes, currently
/// paused/cancelled definitions, and failed dispatches needing attention. Pure read over stored scheduler state
/// + the append-only log — no recomputation; where a count and the log disagree, the log wins.
/// </summary>
public record ReminderOverviewResponse
{
    /// <summary>Upcoming (active, has a pending <c>NextOccurrenceAt</c>) definitions grouped by owner module.</summary>
    public required List<ReminderCategoryCount> UpcomingByOwnerModule { get; init; }

    /// <summary>Upcoming definitions grouped by kind.</summary>
    public required List<ReminderCategoryCount> UpcomingByKind { get; init; }

    /// <summary>Upcoming definitions whose next occurrence falls within the next 7 days.</summary>
    public required int UpcomingNext7Days { get; init; }

    /// <summary>Total active definitions with a pending occurrence.</summary>
    public required int UpcomingTotal { get; init; }

    /// <summary>Definitions currently paused by an admin.</summary>
    public required int PausedCount { get; init; }

    /// <summary>Definitions currently cancelled.</summary>
    public required int CancelledCount { get; init; }

    /// <summary>Dispatch outcomes over the recent window (last 7 days by dispatched-at).</summary>
    public required RecentDispatchOutcomeCounts RecentDispatch { get; init; }

    /// <summary>Effective <c>Failed</c> dispatch rows (not superseded by a later reversal) — the attention list.</summary>
    public required int FailedNeedingAttention { get; init; }
}

/// <summary>A labelled count for an overview breakdown (owner module, kind).</summary>
public record ReminderCategoryCount
{
    public required string Key { get; init; }
    public required int Count { get; init; }
}

/// <summary>Dispatch outcome tallies over the recent window.</summary>
public record RecentDispatchOutcomeCounts
{
    public required int Sent { get; init; }
    public required int Skipped { get; init; }
    public required int Failed { get; init; }
    public required int Reversed { get; init; }
}