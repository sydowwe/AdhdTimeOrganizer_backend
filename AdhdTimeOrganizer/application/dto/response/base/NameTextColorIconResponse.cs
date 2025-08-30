namespace AdhdTimeOrganizer.application.dto.response.@base;

public record NameTextColorIconResponse : NameTextIconResponse
{
    public required string Color { get; init; }
}