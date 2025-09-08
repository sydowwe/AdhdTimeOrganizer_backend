using AdhdTimeOrganizer.application.dto.response.extendable;

namespace AdhdTimeOrganizer.application.dto.response.activityPlanning;

public record TodoListResponse : WithIsDoneResponse
{
    public int? DoneCount { get; set; }
    public int? TotalCount { get; set; }
    public required TaskUrgencyResponse TaskUrgency { get; init; }
}