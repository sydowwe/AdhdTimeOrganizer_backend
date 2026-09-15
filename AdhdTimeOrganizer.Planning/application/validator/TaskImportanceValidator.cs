using AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner;
using FastEndpoints;
using FluentValidation;
using Sydowwe.Framework.domain;

namespace AdhdTimeOrganizer.Planning.application.validator;

public class TaskImportanceValidator : Validator<TaskImportanceRequest>
{
    public TaskImportanceValidator()
    {
        RuleFor(x => x.Text)
            .NotEmpty()
            .MaximumLength(500);

        RuleFor(x => x.Color)
            .NotEmpty()
            .MaximumLength(EntityFieldLengths.Color);
    }
}