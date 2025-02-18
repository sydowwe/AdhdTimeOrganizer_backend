namespace AdhdTimeOrganizer.Common.application.dto.response.@base;

public record TextColorResponse : IdResponse
{
    public required string Text { get; init; }
    public required string Color { get; init; }
}