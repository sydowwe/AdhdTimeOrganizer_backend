using AdhdTimeOrganizer.application.dto.request.generic;

namespace AdhdTimeOrganizer.application.dto.request.@base.table;

public record BaseSortRequest
{
    public required SortByRequest[] SortBy { get; set; }
}