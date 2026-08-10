using System.ComponentModel.DataAnnotations;

namespace AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner;

public record PlannerFilterRequest
{
    public DateTime FilterDate { get; init; }

    [Range(1, 72)]
    public int HourSpan { get; init; }
}