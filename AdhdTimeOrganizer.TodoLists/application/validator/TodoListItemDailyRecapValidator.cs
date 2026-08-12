using AdhdTimeOrganizer.TodoLists.application.dto.request.todoList;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.TodoLists.application.validator;

public class TodoListItemDailyRecapValidator : Validator<TodoListItemDailyRecapRequest>
{
    public TodoListItemDailyRecapValidator()
    {
        // Catches an omitted or unparseable date, which model binding leaves as default(DateOnly)
        // rather than rejecting — without this the recap would silently answer for 0001-01-01.
        RuleFor(x => x.Date)
            .NotEqual(default(DateOnly))
            .WithMessage("A date is required, formatted YYYY-MM-DD.");
    }
}
