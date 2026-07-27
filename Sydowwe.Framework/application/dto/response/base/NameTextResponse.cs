namespace Sydowwe.Framework.application.dto.response.@base;

public record NameTextResponse : NameResponse
{
    public string? Text { get; init; }
}