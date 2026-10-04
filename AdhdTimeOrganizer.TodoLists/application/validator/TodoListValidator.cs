using AdhdTimeOrganizer.TodoLists.application.dto.request.todoList;
using FastEndpoints;
using FluentValidation;
using Sydowwe.Framework.domain;

namespace AdhdTimeOrganizer.TodoLists.application.validator;

public class TodoListValidator : Validator<TodoListRequest>
{
    public TodoListValidator()
    {
        RuleFor(x => x.CategoryId)
            .GreaterThan(0L)
            .When(x => x.CategoryId.HasValue);

        // The list's description. Same limit as the column, so an over-long one is a 400 keyed to
        // `text` rather than a Postgres 22001 the form cannot attach to a field.
        RuleFor(x => x.Text)
            .MaximumLength(EntityFieldLengths.Text);
    }
}