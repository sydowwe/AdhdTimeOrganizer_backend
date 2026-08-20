using AdhdTimeOrganizer.Tracking.application.dto.response.activityTracking.summaryCards;

namespace AdhdTimeOrganizer.Tracking.application.dto.response.activityTracking.unified;

/// <summary>
/// One tracker's standing in a merged request. The three of these are what makes the merged total
/// falsifiable rather than merely smaller: a user who sees less time here than the three dashboards add
/// up to can read off exactly where their browser hour went.
/// </summary>
public record UnifiedSourceStatusDto
{
    /// <summary><c>webExtension</c> / <c>desktop</c> / <c>android</c>.</summary>
    public required string Source { get; init; }

    /// <summary>
    /// Whether the source recorded anything at all in the span — before de-overlapping and regardless
    /// of selection. A source whose every second was displaced still has data, and the filter must not
    /// present that as "not connected".
    /// </summary>
    public required bool HasData { get; init; }

    /// <summary>Attributed to this source after the overlap rule. <c>0</c> for a deselected source.</summary>
    public required int CountedSeconds { get; init; }

    /// <summary>Recorded by this source, credited to another. <c>0</c> for a deselected source.</summary>
    public required int DisplacedSeconds { get; init; }

    /// <summary>
    /// Which source took them, or <c>null</c> when none were displaced. Where more than one source took
    /// time, the one that took the most — the page renders a single line per source and splitting it
    /// further is not wanted.
    /// </summary>
    public required string? DisplacedTo { get; init; }
}

/// <summary>
/// A merged summary card. <c>label</c> replaces the three per-source name fields, and
/// <see cref="Sources"/> says which trackers are behind the number.
/// </summary>
public record UnifiedSummaryCardDto
{
    public required string Label { get; init; }

    /// <summary><c>null</c> renders as no activity rather than as a zero.</summary>
    public ActivityStatDto? Active { get; init; }

    /// <summary>Always <c>null</c> for an item only android saw — that ledger records no background time.</summary>
    public ActivityStatDto? Background { get; init; }

    public required int TotalSeconds { get; init; }

    public required bool IsNew { get; init; }

    public List<string> Sources { get; init; } = [];
}

public record UnifiedPieItemDto
{
    public required string Label { get; init; }
    public required int ActiveSeconds { get; init; }
    public required int BackgroundSeconds { get; init; }
    public required int TotalSeconds { get; init; }

    /// <summary>Ledger rows behind the item, the counterpart of the per-source pies' own <c>entries</c>.</summary>
    public required int Entries { get; init; }

    /// <summary>Never empty for a returned item.</summary>
    public List<string> Sources { get; init; } = [];
}

/// <summary>
/// <b>Present even when <c>items</c> is empty</b> — the client reads it unconditionally, so an absent
/// object is a crash rather than an empty chart.
/// </summary>
public record UnifiedPieTotalsDto
{
    public required int TotalSeconds { get; init; }
    public required int ActiveSeconds { get; init; }
    public required int BackgroundSeconds { get; init; }

    /// <summary>
    /// Distinct items over the <b>whole span</b>, not a sum of per-day counts. Summing per day is
    /// invisible on a single day and reports a week as sevenfold.
    /// </summary>
    public required int TotalItems { get; init; }

    /// <summary>
    /// Likewise distinct over the span, and a session the overlap rule split counts <b>once</b> —
    /// counting the merged fragments would report an artefact of the merge as something the user did.
    /// </summary>
    public required int TotalSessions { get; init; }
}

public record UnifiedPieChartResponse
{
    public List<UnifiedPieItemDto> Items { get; init; } = [];
    public required UnifiedPieTotalsDto Totals { get; init; }
}

public record UnifiedStackedBarsItemDto
{
    public required string Label { get; init; }
    public required int ActiveSeconds { get; init; }
    public required int BackgroundSeconds { get; init; }
    public List<string> Sources { get; init; } = [];
}

/// <summary>
/// One band of the merged stacked bars. <c>windowStart</c> is unique across the response because the
/// bands come from one chronological tiling rather than from a minute-of-day alignment, which over a
/// span would collide between days.
/// </summary>
public record UnifiedStackedBarsWindowDto
{
    public required DateTime WindowStart { get; init; }
    public required DateTime WindowEnd { get; init; }

    /// <summary>
    /// <b>One entry per label per band, merged across sources.</b> A per-(label, source) split would
    /// draw the same application twice in one column in the same colour, and the bars have no room to
    /// explain why; the source dimension is carried by the timeline's lanes and the filter's totals.
    /// </summary>
    public List<UnifiedStackedBarsItemDto> Items { get; init; } = [];
}

public record UnifiedTimelineSessionDto
{
    /// <summary>Unique across the <b>whole</b> response, not merely within a lane.</summary>
    public required int Id { get; set; }

    public required string Label { get; init; }
    public required DateTime StartedAt { get; init; }
    public required DateTime EndedAt { get; init; }
    public required int DurationSeconds { get; init; }
    public required int TotalSeconds { get; init; }

    /// <summary>The most-seen page of a browsing session; <c>null</c> on the other two lanes.</summary>
    public string? Url { get; init; }
}

/// <summary>
/// The merged timeline. Its lanes <b>are</b> the three trackers — not the active/detail/background
/// split the per-source timelines use — and they render top to bottom in precedence order, so the
/// overlap resolution reads down the chart.
/// </summary>
public record UnifiedTimelineResponse
{
    public List<UnifiedTimelineSessionDto> WebExtensionSessions { get; init; } = [];
    public List<UnifiedTimelineSessionDto> DesktopSessions { get; init; } = [];
    public List<UnifiedTimelineSessionDto> AndroidSessions { get; init; } = [];
}
