using AdhdTimeOrganizer.application.dto.request.taskPlanner;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.application.validator;

public class PatchPlannerTaskStatusValidator : Validator<PatchPlannerTaskStatusRequest>
{
    public PatchPlannerTaskStatusValidator()
    {
        RuleFor(x => x.Status).IsInEnum();

        RuleFor(x => x.ActualStartTime!)
            .Must(t => t.Hours is >= 0 and <= 23 && t.Minutes is >= 0 and <= 59)
            .WithMessage("ActualStartTime hours must be 0–23 and minutes 0–59.")
            .When(x => x.ActualStartTime != null);

        RuleFor(x => x.ActualEndTime!)
            .Must(t => t.Hours is >= 0 and <= 23 && t.Minutes is >= 0 and <= 59)
            .WithMessage("ActualEndTime hours must be 0–23 and minutes 0–59.")
            .When(x => x.ActualEndTime != null);

        RuleFor(x => x).Must(req =>
            {
                if (req.ActualStartTime == null || req.ActualEndTime == null)
                    return true;
                return req.ActualStartTime.ToTimeOnly() < req.ActualEndTime.ToTimeOnly();
            })
            .WithMessage("ActualStartTime must be before ActualEndTime.")
            .When(x => x.ActualStartTime != null && x.ActualEndTime != null);
    }
}
