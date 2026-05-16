using AdhdTimeOrganizer.application.dto.request.todoList;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.application.validator;

public class TodoListValidator : Validator<TodoListRequest>
{
    public TodoListValidator()
    {
        RuleFor(x => x.CategoryId)
            .GreaterThan(0L)
            .When(x => x.CategoryId.HasValue);
    }
}
