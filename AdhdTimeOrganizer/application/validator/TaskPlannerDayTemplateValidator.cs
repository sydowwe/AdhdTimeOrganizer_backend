using AdhdTimeOrganizer.application.dto.request.taskPlanner.template;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.application.validator;

public class TaskPlannerDayTemplateValidator : Validator<TaskPlannerDayTemplateRequest>
{
    public TaskPlannerDayTemplateValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(x => x.Description)
            .MaximumLength(500)
            .When(x => x.Description != null);

        RuleFor(x => x.Icon)
            .MaximumLength(50)
            .When(x => x.Icon != null);

        RuleFor(x => x.DefaultWakeUpTime)
            .Must(t => t!.Hours is >= 0 and <= 23 && t.Minutes is >= 0 and <= 59)
            .When(x => x.DefaultWakeUpTime != null)
            .WithMessage("DefaultWakeUpTime hours must be 0–23 and minutes 0–59.");

        RuleFor(x => x.DefaultBedTime)
            .Must(t => t!.Hours is >= 0 and <= 23 && t.Minutes is >= 0 and <= 59)
            .When(x => x.DefaultBedTime != null)
            .WithMessage("DefaultBedTime hours must be 0–23 and minutes 0–59.");
    }
}
