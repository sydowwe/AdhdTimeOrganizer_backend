namespace AdhdTimeOrganizer.TodoLists.application.dto.request.todoList;

/// <summary>Writes the to-do per-user settings.</summary>
public record UserTodoListSettingsRequest
{
    /// <summary>Minutes before a timed to-do's due instant its reminder fires; 0 is "at the due time".</summary>
    public required int DueReminderMinutesBefore { get; init; }
}
