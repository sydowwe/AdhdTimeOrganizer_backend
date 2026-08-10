using AdhdTimeOrganizer.TodoLists.application.dto.request.todoList;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.TodoLists.application.validator;

public class TodoListValidator : Validator<TodoListRequest>
{
    public TodoListValidator()
    {
        RuleFor(x => x.CategoryId)
            .GreaterThan(0L)
            .When(x => x.CategoryId.HasValue);
    }
}