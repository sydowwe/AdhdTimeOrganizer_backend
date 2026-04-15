namespace AdhdTimeOrganizer.application.dto.response.toDoList;

public record PeriodCompletionRecord(
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    int CompletedCount,
    int TotalCount
);