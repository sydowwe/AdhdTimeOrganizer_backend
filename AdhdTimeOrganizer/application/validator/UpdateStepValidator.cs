using AdhdTimeOrganizer.application.dto.request.todoList;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.application.validator;

public class UpdateStepValidator : Validator<UpdateStepRequest>
{
    public UpdateStepValidator()
    {
        RuleFor(x => x.ItemId)
            .GreaterThan(0);

        RuleFor(x => x.StepId)
            .NotEmpty();

        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(255);

        RuleFor(x => x.Note)
            .MaximumLength(1000)
            .When(x => x.Note != null);
    }
}
