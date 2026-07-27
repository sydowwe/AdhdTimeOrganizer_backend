using Sydowwe.Framework.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.filter;

public record CategoryFilterRequest : IFilterRequest
{
    public string? Name { get; set; }
    public string? Text { get; set; }
    public string? Color { get; set; }
}