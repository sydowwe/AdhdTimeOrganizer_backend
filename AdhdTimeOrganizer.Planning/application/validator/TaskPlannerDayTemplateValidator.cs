using AdhdTimeOrganizer.Core.application.validator;
using AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner.template;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.Planning.application.validator;

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

        RuleFor(x => x.DefaultWakeUpTime!).ApplyTimeOfDayRules().When(x => x.DefaultWakeUpTime != null);
        RuleFor(x => x.DefaultBedTime!).ApplyTimeOfDayRules().When(x => x.DefaultBedTime != null);
    }
}