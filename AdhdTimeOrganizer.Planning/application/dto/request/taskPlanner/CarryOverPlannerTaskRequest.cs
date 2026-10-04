namespace AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner;

/// <summary>Body of <c>POST /planner-task/{id}/carry-over</c>.</summary>
public record CarryOverPlannerTaskRequest
{
    /// <summary>
    /// The day to carry the task to. Must be after the task's current day — carrying over moves an unfinished
    /// task <i>forward</i>; putting one back on an earlier day is an ordinary move. Created if the user has no
    /// calendar row for it yet, like every other write that names a day by date.
    /// </summary>
    public required DateOnly Date { get; init; }
}
