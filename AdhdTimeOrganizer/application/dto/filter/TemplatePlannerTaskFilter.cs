using AdhdTimeOrganizer.application.dto.dto;
using Sydowwe.Framework.application.dto.dto;
using Sydowwe.Framework.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.filter;

public record TemplatePlannerTaskFilter : IFilterRequest
{
    public required long TemplateId { get; init; }

    public required TimeDto From { get; init; }


    public required TimeDto Until { get; init; }
}