using AdhdTimeOrganizer.TodoLists.application.dto.request.todoList;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.TodoLists.application.validator;

public class StepItemAndStepIdValidator : Validator<StepItemAndStepIdRequest>
{
    public StepItemAndStepIdValidator()
    {
        RuleFor(x => x.ItemId)
            .GreaterThan(0);

        RuleFor(x => x.StepId)
            .NotEmpty();
    }
}