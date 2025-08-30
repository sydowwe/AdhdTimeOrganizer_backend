using AdhdTimeOrganizer.application.dto.request.generic;

namespace AdhdTimeOrganizer.application.dto.request.@base.table;

public record SortPaginateRequest
{
    public required int ItemsPerPage { get; set; }

    public required int Page { get; set; }
    public required SortByRequest[] SortBy { get; set; }
}