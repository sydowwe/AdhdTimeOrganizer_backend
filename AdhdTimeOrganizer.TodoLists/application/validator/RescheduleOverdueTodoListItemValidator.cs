using AdhdTimeOrganizer.TodoLists.application.dto.request.todoList;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.TodoLists.application.validator;

public class RescheduleOverdueTodoListItemValidator : Validator<RescheduleOverdueTodoListItemRequest>
{
    /// <summary>A year out is already a decision to drop the task; anything further is a typo.</summary>
    public const int MaxDays = 365;

    public RescheduleOverdueTodoListItemValidator()
    {
        RuleFor(x => x.Days).InclusiveBetween(0, MaxDays);
        RuleFor(x => x.ListId).GreaterThan(0).When(x => x.ListId is not null);
    }
}
