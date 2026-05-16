using AdhdTimeOrganizer.application.dto.request.taskPlanner;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.application.validator;

public class PlannerTaskChangeSpanValidator : Validator<PlannerTaskChangeSpanRequest>
{
    public PlannerTaskChangeSpanValidator()
    {
        RuleFor(x => x.StartTime)
            .Must(t => t.Hours is >= 0 and <= 23 && t.Minutes is >= 0 and <= 59)
            .WithMessage("StartTime hours must be 0–23 and minutes 0–59.");

        RuleFor(x => x.EndTime)
            .Must(t => t.Hours is >= 0 and <= 23 && t.Minutes is >= 0 and <= 59)
            .WithMessage("EndTime hours must be 0–23 and minutes 0–59.");

        RuleFor(x => x).Must(req => req.StartTime.ToTimeOnly() < req.EndTime.ToTimeOnly())
            .WithMessage("StartTime must be before EndTime.");
    }
}
