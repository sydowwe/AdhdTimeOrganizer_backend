namespace Sydowwe.Framework.application.dto.request.@base;

public record NameTextIconRequest : NameTextRequest
{
    public string? Icon { get; init; }
}