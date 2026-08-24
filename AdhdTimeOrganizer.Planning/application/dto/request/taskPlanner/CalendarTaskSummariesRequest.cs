namespace AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner;

/// <summary>
/// The same window the caller already sends to <c>calendar/filter</c> — both ends inclusive, so the two
/// requests that a date-range change fires cover exactly the same days.
/// </summary>
public record CalendarTaskSummariesRequest
{
    public required DateOnly From { get; init; }

    public required DateOnly Until { get; init; }
}
