using AdhdTimeOrganizer.application.dto.response.extendable;

namespace AdhdTimeOrganizer.application.dto.response.activityPlanning;

public record BaseTodoListResponse : WithIsDoneResponse
{
    public long DisplayOrder { get; init; }

    public int? DoneCount { get; set; }
    public int? TotalCount { get; set; }
}