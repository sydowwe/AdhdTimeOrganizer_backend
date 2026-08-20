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
    public required List<string> Pages { get; init; }
    public required int Entries { get; init; }
}