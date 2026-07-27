namespace Sydowwe.Framework.application.dto.request.@base;

public record NameTextRequest
{
    public required string Name { get; init; }

    public string? Text { get; init; }
}