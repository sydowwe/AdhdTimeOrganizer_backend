namespace AdhdTimeOrganizer.Tracking.application.dto.response.activityTracking.pieChart;

public record DomainPieDataDto : IDashboardItem
{
    public required string Domain { get; init; }

    /// <inheritdoc />
    public string Key => Domain;

    /// <inheritdoc />
    public string Label => Domain;

    public required int ActiveSeconds { get; init; }
    public required int BackgroundSeconds { get; init; }
    public required int TotalSeconds { get; init; }

    // No page list. It was every distinct URL of the domain over the whole span, which no client renders
    // (PERF-5). The ranked, timed list is GET …/domain-details, and the count is DayTotalsResponse.TotalPages.

    public required int Entries { get; init; }
}