using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;

namespace AdhdTimeOrganizer.Routines.application.dto.response.todoList;

/// <summary>
/// One elapsed period in the heatmap. <paramref name="IsFrozen"/> is a third state alongside met/missed, not a
/// flavour of either: the counts stay as they were recorded, and only the streak walk treats the period as
/// neutral. Defaulted so a client that predates freezes reads the field as false.
/// </summary>
public record PeriodCompletionRecord(
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    int CompletedCount,
    int TotalCount,
    bool IsFrozen = false
)
{
    public static PeriodCompletionRecord From(RoutinePeriodCompletion completion) =>
        new(completion.PeriodStart, completion.PeriodEnd, completion.CompletedCount, completion.TotalCount, completion.IsFrozen);
}
