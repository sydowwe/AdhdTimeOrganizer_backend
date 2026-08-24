using Sydowwe.Framework.application.dto.dto;

namespace AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner;

/// <summary>
/// Every planner task in a date range, grouped by day — what the month grid needs to draw its cells, in one
/// round trip instead of one <c>planner-task/filter</c> per visible day.
/// <para>
/// Deliberately <b>not</b> <see cref="PlannerTaskResponse"/>: the cells read a task's span, its lane and its
/// activity's name and colour, and nothing else. Reusing the full response would ship notes, location, status,
/// importance and a nested activity/role/category object for every task of every day in the month, which is
/// most of what this endpoint exists to stop sending.
/// </para>
/// </summary>
public record CalendarTaskSummariesResponse
{
    /// <summary>
    /// One entry per day that has at least one task. Days with none are omitted rather than sent empty — the
    /// caller keys these into a per-day map and a missing key already means "no tasks".
    /// </summary>
    public required List<CalendarDayTaskSummaryResponse> Days { get; init; }
}

public record CalendarDayTaskSummaryResponse
{
    /// <summary>The <c>Calendar.Id</c> the day's tasks hang off — the same id <c>calendar/filter</c> returns.</summary>
    public required long CalendarId { get; init; }

    /// <summary>The day's tasks, ordered by start time.</summary>
    public required List<PlannerTaskSummaryResponse> Tasks { get; init; }
}

public record PlannerTaskSummaryResponse
{
    public required long Id { get; init; }

    public required TimeDto StartTime { get; init; }

    public required TimeDto EndTime { get; init; }

    /// <summary>Which timeline lane the segment belongs in.</summary>
    public required bool IsBackground { get; init; }

    /// <summary>The activity role's colour — same value <see cref="PlannerTaskResponse.Color"/> carries.</summary>
    public required string Color { get; init; }

    /// <summary>
    /// Flattened from <c>Activity.Name</c>. The cell tooltip reads only the name, so nesting a whole
    /// <c>ActivityResponse</c> here would send a role and a category object per task for nothing.
    /// </summary>
    public required string ActivityName { get; init; }
}
