using AdhdTimeOrganizer.application.dto.response.@base;

namespace AdhdTimeOrganizer.application.dto.response.activityPlanning;
public record TaskImportanceResponse : TextColorResponse
{
    public required int Importance { get; init; }
}
