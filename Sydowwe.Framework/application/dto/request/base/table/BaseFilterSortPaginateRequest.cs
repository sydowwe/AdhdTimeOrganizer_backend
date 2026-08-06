using Sydowwe.Framework.application.dto.request.@interface;

namespace Sydowwe.Framework.application.dto.request.@base.table;

public record BaseFilterSortPaginateRequest<TFilter> : BaseFilterSortRequest<TFilter>
    where TFilter : IFilterRequest
{
    public required int ItemsPerPage { get; set; }

    public required int Page { get; set; }
}