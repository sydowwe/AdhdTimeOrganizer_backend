using AdhdTimeOrganizer.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.filter;

public record TodoListFilterRequest : IFilterRequest
{
    public string? Name { get; set; }
    /// <summary>null = all, -1 = uncategorized, &gt;0 = specific category</summary>
    public long? CategoryId { get; set; }
}