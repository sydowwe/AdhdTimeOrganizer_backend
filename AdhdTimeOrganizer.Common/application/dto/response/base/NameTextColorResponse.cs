namespace AdhdTimeOrganizer.Common.application.dto.response.@base;

public record NameTextColorResponse : NameTextResponse
{
    public required string Color { get; init; }
}