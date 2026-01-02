using AdhdTimeOrganizer.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.request.filter;

public record LookupFilter : IFilterRequest
{
    public string? Text { get; set; }
}