using Sydowwe.Framework.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.filter;

public record TaskImportanceFilterRequest : IFilterRequest
{
    public string? Text { get; set; }
    public string? Color { get; set; }
    public int? MinImportance { get; set; }
    public int? MaxImportance { get; set; }
}