using AdhdTimeOrganizer.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.filter;

public class CategoryFilterRequest : IFilterRequest
{
    public string? Name { get; set; }
    public string? Text { get; set; }
    public string? Color { get; set; }
    public long? UserId { get; set; }
}
