namespace AdhdTimeOrganizer.application.dto.response.todoList;

public record PeriodCompletionRecord(
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    int CompletedCount,
    int TotalCount
);