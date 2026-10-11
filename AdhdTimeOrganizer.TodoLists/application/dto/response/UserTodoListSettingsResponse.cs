using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;

namespace AdhdTimeOrganizer.TodoLists.application.dto.response.todoList;

/// <summary>
/// The to-do per-user settings. A user who has never touched them has no row, and gets this with every field at
/// its default rather than a 404.
/// </summary>
public record UserTodoListSettingsResponse
{
    public required int DueReminderMinutesBefore { get; init; }

    public static UserTodoListSettingsResponse FromEntity(UserTodoListSettings? entity) =>
        new()
        {
            DueReminderMinutesBefore = entity?.DueReminderMinutesBefore ?? UserTodoListSettings.DefaultDueReminderMinutesBefore
        };
}
