using AdhdTimeOrganizer.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.filter;

public class LookupFilterRequest : IFilterRequest
{
    public string? Text { get; set; }
}
