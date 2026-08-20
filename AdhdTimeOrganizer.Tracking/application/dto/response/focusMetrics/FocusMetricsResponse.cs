namespace AdhdTimeOrganizer.Tracking.application.dto.response.activityTracking.focusMetrics;

/// <summary>The longest unbroken block, or <c>null</c> when the span holds no sessions at all.</summary>
public record FocusBlockDto
{
    /// <summary>
    /// The display name of the item — <c>domain</c> / <c>productName</c> / <c>appLabel</c>, matching
    /// what <c>summary-cards</c> reports for the same item, so the two agree on screen.
    /// </summary>
    public required string Label { get; init; }

    public required DateTime StartedAt { get; init; }
    public required DateTime EndedAt { get; init; }

    /// <summary>Wall clock from the block's first start to its last end, tolerated interruptions included.</summary>
    public required double Seconds { get; init; }
}

/// <summary>
/// The user's own recent self, in the same units as the figures beside it.
///
/// <para><b>Averages, not a <c>percentChange</c>.</b> The summary-cards contract returns both and the
/// temptation is to match it here; these numbers are rendered as "87 · typically 61", deliberately
/// without a percentage, an arrow or a colour, because a fragmentation number shown as a red +42% is a
/// verdict on someone's day and this app is for people who procrastinate. A <c>percentChange</c> field
/// would simply go unread.</para>
///
/// <para>Every member is individually nullable: a lookback can hold sessions without holding, say, a
/// single day with two of them, and an absent comparison renders as no comparison rather than as a
/// zero.</para>
/// </summary>
public record FocusBaselineDto
{
    public double? SwitchCount { get; init; }
    public double? MedianSessionSeconds { get; init; }
    public double? LongestBlockSeconds { get; init; }
    public double? LongestGapSeconds { get; init; }
}

public record FocusMetricsResponse
{
    public required int SessionCount { get; init; }

    /// <summary>
    /// Days in the span with at least one session. Present so the client can render a range figure per
    /// day — "87 switches" over a week is not a number anyone can read — and it counts active days
    /// rather than span days so the ones the user was away do not dilute it.
    /// </summary>
    public required int DaysWithActivity { get; init; }

    public required int SwitchCount { get; init; }

    /// <summary>May be fractional: an even session count takes the mean of the two middle values.</summary>
    public required double MedianSessionSeconds { get; init; }

    /// <summary><c>null</c> when no day in the span holds two sessions, so there is no interior gap.</summary>
    public required double? LongestGapSeconds { get; init; }

    public required FocusBlockDto? LongestBlock { get; init; }

    /// <summary><c>null</c> when the request sent no baseline, or when there is not enough history.</summary>
    public required FocusBaselineDto? Baseline { get; init; }
}
