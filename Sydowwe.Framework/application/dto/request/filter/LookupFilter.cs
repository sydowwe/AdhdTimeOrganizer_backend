using Sydowwe.Framework.application.dto.request.@interface;

namespace Sydowwe.Framework.application.dto.request.filter;

public record LookupFilter : IFilterRequest
{
    public string? Text { get; set; }
}