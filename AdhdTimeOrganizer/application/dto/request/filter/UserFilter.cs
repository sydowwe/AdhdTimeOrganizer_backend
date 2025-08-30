using AdhdTimeOrganizer.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.request.filter;

public record UserFilter : IFilterRequest
{
    public string? Name { get; set; }
    public string? PhoneNumber { get; set; }
}