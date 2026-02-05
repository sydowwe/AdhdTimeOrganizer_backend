namespace AdhdTimeOrganizer.application.dto.dto;

public record WebExtensionEntryDto
{
    public required string Domain { get; set; }
    public required string Url { get; set; }
    public required int ActiveSeconds { get; set; }
    public required int BackgroundSeconds { get; set; }
}