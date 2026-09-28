namespace AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner;

/// <summary>
/// What the user skipped over a date range, by reason and by planned start hour — the range half of the
/// skip-reason insight, rendered under the plan-vs-actual trend. The single-day half needs no server: the day
/// view already holds every task's status and skip reason.
/// <para>
/// <b><see cref="SkippedCount"/> of 0 means "say nothing"</b>, the same convention as the trend's
/// <c>TaskCount</c>. There is no separate "no data" flag.
/// </para>
/// <para>
/// <b>Not the trend's population.</b> The trend excludes skipped tasks; this is built from every non-background
/// task on a day that is over, in any status. The two share only the range rules — do not compare their
/// numbers.
/// </para>
/// </summary>
public record SkipSummaryResponse
{
    /// <summary>Skipped (Cancelled) tasks in the range. Equals the sum of <see cref="Reasons"/> counts and of <see cref="ByStartHour"/> skipped counts.</summary>
    public required int SkippedCount { get; init; }

    /// <summary>
    /// Every reason group, grouped by exact stored value — no folding and no cap, because the client folds once
    /// more and a truncated list could hide what would have been the largest group. Unordered.
    /// </summary>
    public required List<SkipReasonCountResponse> Reasons { get; init; }

    /// <summary>Hours of the day with at least one planned task, ascending. Hours with nothing planned are omitted.</summary>
    public required List<SkipHourCountResponse> ByStartHour { get; init; }
}

public record SkipReasonCountResponse
{
    /// <summary>The stored free-text reason, exactly. Null on a key group, and on the no-reason group.</summary>
    public required string? Reason { get; init; }

    /// <summary>
    /// <c>"noTime" | "notRelevant" | "noEnergy"</c> on a built-in group, else null. Both this and
    /// <see cref="Reason"/> null is the no-reason group: skipped with no reason.
    /// </summary>
    public required string? ReasonKey { get; init; }

    /// <summary>At least 1.</summary>
    public required int Count { get; init; }
}

public record SkipHourCountResponse
{
    /// <summary>0–23, the hour of the task's planned <c>StartTime</c> as stored. A task wrapping midnight counts where it starts.</summary>
    public required int Hour { get; init; }

    /// <summary>Every eligible task planned to start in this hour, in any status — the base rate. At least 1.</summary>
    public required int PlannedCount { get; init; }

    /// <summary>Of <see cref="PlannedCount"/>, how many were skipped.</summary>
    public required int SkippedCount { get; init; }
}
