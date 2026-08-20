namespace AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking;

/// <summary>
/// The stacked-bars request, shared by all three sources.
///
/// <para>It was two classes — this one, named for the web extension while the desktop dashboard bound
/// it too, and an android copy that differed only in <c>MinSeconds</c> being a <c>long</c>. There is
/// nothing source-specific in a bucket width, so the split was a naming accident rather than a
/// distinction; the response shapes, which genuinely do differ per source, stay separate.</para>
/// </summary>
public record StackedBarsRequest : DateRangeAndTimeRangeDto
{
    /// <summary>
    /// Bucket width. Under a day the buckets tile each day's time-of-day window starting at
    /// <c>From</c>; a day or more and they tile the span in whole days. Nothing between 480 and 1440 is
    /// meaningful — see <c>DailyWindowSet.Tile</c> — and the validator pins the accepted set.
    /// </summary>
    public required int WindowMinutes { get; init; }

    /// <summary>Items below this many seconds in a band are folded into that band's "other" bucket.</summary>
    public int? MinSeconds { get; init; }
}
