using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.dto.dto;
using AdhdTimeOrganizer.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.filter;

public class TemplatePlannerTaskFilter : IFilterRequest
{
    [Required]
    public required long TemplateId { get; init; }
    [Required]
    public required TimeDto From { get; init; }

    [Required]
    public required TimeDto Until { get; init; }
}