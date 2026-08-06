using Sydowwe.Framework.application.dto.request.@interface;

namespace Sydowwe.Framework.application.dto.request.filter;

public record UserFilter : IFilterRequest
{
    public string? Name { get; set; }
    public string? PhoneNumber { get; set; }
}