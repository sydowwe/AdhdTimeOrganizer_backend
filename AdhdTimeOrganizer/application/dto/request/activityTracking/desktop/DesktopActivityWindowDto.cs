namespace AdhdTimeOrganizer.application.dto.request.activityTracking.desktop;

public sealed record DesktopActivityWindowDto
{
    public required DateTime WindowStart { get; init; }
    public required List<DesktopActivityEntryDto> Entries { get; init; }
}
