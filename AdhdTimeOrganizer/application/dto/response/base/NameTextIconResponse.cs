namespace AdhdTimeOrganizer.application.dto.response.@base;

public record NameTextIconResponse : NameTextResponse
{
    public string? Icon { get; init; }
}