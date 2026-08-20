namespace AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.android;

public record AndroidStackedBarsRequest : DateRangeAndTimeRangeDto
{
    /// <inheritdoc cref="WebExtensionStackedBarsRequest.WindowMinutes"/>
    public required int WindowMinutes { get; init; }

    public long? MinSeconds { get; init; }
}
