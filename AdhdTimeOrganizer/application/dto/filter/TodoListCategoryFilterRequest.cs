using AdhdTimeOrganizer.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.filter;

public record TodoListCategoryFilterRequest : IFilterRequest
{
    public string? Name { get; set; }
    public bool HideEmpty { get; set; }
}