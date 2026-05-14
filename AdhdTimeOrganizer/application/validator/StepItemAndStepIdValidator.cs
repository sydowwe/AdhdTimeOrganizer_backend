using AdhdTimeOrganizer.application.dto.request.toDoList;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.application.validator;

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
