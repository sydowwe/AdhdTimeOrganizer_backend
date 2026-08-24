namespace AdhdTimeOrganizer.Planning.application.helper;

/// <summary>
/// The ceilings on the batched calendar entry points. They exist because these requests are the only planner
/// routes whose cost scales with something the client picks: a day count, or a day count times a task count.
/// The per-day endpoints they replace were bounded by the browser's own connection limit; batching removes
/// that accidental bound, so the bound has to be stated here instead.
/// <para>
/// A year is the ceiling rather than a month because the caller may legitimately be looking at a multi-month
/// range, and because <c>DateRangeDto.MaxCustomRangeDays</c> already settled on the same number for the same
/// reason on the history dashboards. It is a sanity bound, not a product limit — the calendar view selects
/// within one month.
/// </para>
/// </summary>
public static class PlannerBatchLimits
{
    /// <summary>Most days one batched write may name.</summary>
    public const int MaxCalendarIds = 366;

    /// <summary>Most days one task-summary read may span, counted inclusively.</summary>
    public const int MaxSummaryRangeDays = 366;
}
