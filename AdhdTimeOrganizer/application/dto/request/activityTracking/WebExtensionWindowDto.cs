namespace AdhdTimeOrganizer.application.dto.dto;

public record WebExtensionWindowDto
{
    public required DateTime WindowStart { get; set; }
    public required int WindowMinutes { get; set; }
    public required bool IsFinal { get; set; }
    public required List<WebExtensionEntryDto> Activities { get; set; }
}