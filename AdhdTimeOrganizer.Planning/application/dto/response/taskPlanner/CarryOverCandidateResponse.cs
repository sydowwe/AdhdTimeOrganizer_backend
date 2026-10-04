namespace AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner;

/// <summary>An open task from an earlier day, with the day it is on — <see cref="PlannerTaskResponse"/> carries only the calendar id.</summary>
public record CarryOverCandidateResponse
{
    public required DateOnly Date { get; init; }
    public required PlannerTaskResponse Task { get; init; }
}
