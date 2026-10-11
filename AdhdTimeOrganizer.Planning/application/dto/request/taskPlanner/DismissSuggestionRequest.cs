using AdhdTimeOrganizer.Planning.domain.model.@enum;

namespace AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner;

/// <summary>
/// Body of <c>POST /repeating-planner-task/suggestions/dismiss</c> — the pattern a suggestion row came from, as
/// the row already describes it: its activity, its <c>recurrenceType</c>, and the one weekday or day of the
/// month in its <c>scheduledDays</c> / <c>scheduledDates</c>.
/// </summary>
public record DismissSuggestionRequest
{
    public long ActivityId { get; init; }

    /// <summary><see cref="RecurrenceType.DayOfWeek"/> or <see cref="RecurrenceType.DayOfMonth"/>.</summary>
    public RecurrenceType PatternType { get; init; }

    /// <summary>ISO weekday (1 = Monday … 7 = Sunday) or day of the month (1–31).</summary>
    public int PatternValue { get; init; }
}
