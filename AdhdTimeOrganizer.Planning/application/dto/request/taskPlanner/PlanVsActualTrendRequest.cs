namespace AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner;

/// <summary>
/// The window the trend is computed over — both ends inclusive, the same shape and the same format
/// <c>CalendarFilter</c> and <c>CalendarTaskSummariesRequest</c> already use, so a range change fires one
/// request per view rather than one per day.
/// <para>
/// There is no calendar id here, and that is the point: <c>planner-task/filter</c> takes exactly one, so a
/// cross-day question asked through it costs one round trip per visible day.
/// </para>
/// </summary>
public record PlanVsActualTrendRequest
{
    public required DateOnly From { get; init; }

    public required DateOnly Until { get; init; }
}
