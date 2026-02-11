namespace AdhdTimeOrganizer.application.dto.response.activityTracking.pieChart;

public record DomainPieDataDto
{
    public required string Domain { get; init; }
    public required int ActiveSeconds { get; init; }
    public required int BackgroundSeconds { get; init; }
    public required int TotalSeconds { get; init; }
    public required List<string> Pages { get; init; }
    public required int Entries { get; init; }
}
