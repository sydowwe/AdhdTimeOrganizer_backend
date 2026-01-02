using AdhdTimeOrganizer.application.dto.request.generic;
using AdhdTimeOrganizer.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.request.@base.table;

public record BaseFilterSortRequest<TFilter> : BaseFilterRequest<TFilter>
    where TFilter : IFilterRequest
{
    public required SortByRequest[] SortBy { get; set; }
}