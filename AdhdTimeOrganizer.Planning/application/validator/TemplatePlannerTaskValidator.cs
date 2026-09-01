using AdhdTimeOrganizer.Core.application.validator;
using AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner.template;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.Planning.application.validator;

public class TemplatePlannerTaskValidator : Validator<TemplatePlannerTaskRequest>
{
    public TemplatePlannerTaskValidator()
    {
        RuleFor(x => x.ActivityId)
            .GreaterThan(0);

        RuleFor(x => x.ImportanceId)
            .GreaterThan(0)
            .When(x => x.ImportanceId.HasValue);

        RuleFor(x => x.TemplateId)
            .GreaterThan(0);

        RuleFor(x => x.StartTime).ApplyTimeOfDayRules();
        RuleFor(x => x.EndTime).ApplyTimeOfDayRules();

        RuleFor(x => x.Location)
            .MaximumLength(200)
            .When(x => x.Location != null);

        RuleFor(x => x.Notes)
            .MaximumLength(1000)
            .When(x => x.Notes != null);
    }
}