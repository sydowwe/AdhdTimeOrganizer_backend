using AdhdTimeOrganizer.Core.application.validator;
using AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.Planning.application.validator;

public class PlannerTaskChangeSpanValidator : Validator<PlannerTaskChangeSpanRequest>
{
    public PlannerTaskChangeSpanValidator()
    {
        RuleFor(x => x.StartTime).ApplyTimeOfDayRules();
        RuleFor(x => x.EndTime).ApplyTimeOfDayRules();

        RuleFor(x => x).Must(req => req.StartTime.IsBefore(req.EndTime))
            .WithMessage("StartTime must be before EndTime.");
    }
}