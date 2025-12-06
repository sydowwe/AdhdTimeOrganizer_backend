using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.request.taskPlanner.template;

public record TemplatePlannerTaskRequest : BasePlannerTaskRequest, IMyRequest
{
    [Required]
    public required long TemplateId { get; init; }
}