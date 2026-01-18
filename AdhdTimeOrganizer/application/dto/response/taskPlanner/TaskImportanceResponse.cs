using AdhdTimeOrganizer.application.dto.response.@base;

namespace AdhdTimeOrganizer.application.dto.response.taskPlanner;
public record TaskImportanceResponse : TextColorIconResponse
{
    public required int Importance { get; init; }
}
