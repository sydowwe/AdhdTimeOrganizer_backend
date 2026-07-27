using Sydowwe.Framework.application.dto.request.@interface;

namespace Sydowwe.Framework.application.dto.request.@base.table;

public record BaseFilterRequest<TFilter>
    where TFilter : IFilterRequest
{
    public required bool UseFilter { get; set; } = false;
    public required TFilter? Filter { get; set; }
}