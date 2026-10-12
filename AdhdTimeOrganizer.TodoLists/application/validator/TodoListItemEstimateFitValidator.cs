using AdhdTimeOrganizer.TodoLists.application.dto.request.todoList;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.TodoLists.application.validator;

public class TodoListItemEstimateFitValidator : Validator<TodoListItemEstimateFitRequest>
{
    public TodoListItemEstimateFitValidator()
    {
        RuleFor(x => x.GroupBy)
            .NotNull()
            .IsInEnum()
            .WithMessage("groupBy is required: list, category or priority.");
    }
}
