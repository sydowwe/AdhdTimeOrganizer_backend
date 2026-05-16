using AdhdTimeOrganizer.application.dto.request.taskPlanner;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.application.validator;

public class TaskImportanceValidator : Validator<TaskImportanceRequest>
{
    public TaskImportanceValidator()
    {
        RuleFor(x => x.Text)
            .NotEmpty()
            .MaximumLength(500);

        RuleFor(x => x.Color)
            .NotEmpty()
            .MaximumLength(7);
    }
}
