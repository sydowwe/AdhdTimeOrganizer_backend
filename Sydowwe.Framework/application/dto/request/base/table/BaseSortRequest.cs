using Sydowwe.Framework.application.dto.request.generic;

namespace Sydowwe.Framework.application.dto.request.@base.table;

public record BaseSortRequest
{
    public required SortByRequest[] SortBy { get; set; }
}