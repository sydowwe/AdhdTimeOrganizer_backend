using Sydowwe.Framework.application.dto.request.generic;
using Sydowwe.Framework.application.dto.request.@interface;

namespace Sydowwe.Framework.application.dto.request.@base.table;

public record BaseFilterSortRequest<TFilter> : BaseFilterRequest<TFilter>
    where TFilter : IFilterRequest
{
    public required SortByRequest[] SortBy { get; set; }
}