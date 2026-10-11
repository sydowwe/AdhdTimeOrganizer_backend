using AdhdTimeOrganizer.TodoLists.application.dto.request.todoList;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.TodoLists.application.validator;

public class UserTodoListSettingsValidator : Validator<UserTodoListSettingsRequest>
{
    public UserTodoListSettingsValidator()
    {
        RuleFor(x => x.DueReminderMinutesBefore)
            .InclusiveBetween(0, UserTodoListSettings.MaxDueReminderMinutesBefore);
    }
}
