namespace AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking;

public record WebExtensionStackedBarsRequest : DateRangeAndTimeRangeDto
{
    /// <summary>
    /// Bucket width. Under a day the buckets tile each day's time-of-day window starting at
    /// <c>From</c>; a day or more and they tile the span in whole days. Nothing between 480 and 1440 is
    /// meaningful — see <c>DailyWindowSet.Tile</c> — and the validator pins the accepted set.
    /// </summary>
    public required int WindowMinutes { get; init; }

    public int? MinSeconds { get; init; } // Filter out domains below threshold
}
