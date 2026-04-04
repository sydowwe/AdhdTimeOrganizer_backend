namespace AdhdTimeOrganizer.application.dto.response.taskPlanner.template;

public record TemplatePlannerTaskResponse : BasePlannerTaskResponse
{
    public required long TemplateId { get; init; }
    public required string Color { get; init; }
}