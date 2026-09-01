using AdhdTimeOrganizer.Core.application.validator;
using AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.Planning.application.validator;

public class PatchPlannerTaskStatusValidator : Validator<PatchPlannerTaskStatusRequest>
{
    public PatchPlannerTaskStatusValidator()
    {
        RuleFor(x => x.Status).IsInEnum();

        RuleFor(x => x.ActualStartTime!).ApplyTimeOfDayRules().When(x => x.ActualStartTime != null);
        RuleFor(x => x.ActualEndTime!).ApplyTimeOfDayRules().When(x => x.ActualEndTime != null);

        RuleFor(x => x).Must(req =>
            {
                if (req.ActualStartTime == null || req.ActualEndTime == null)
                    return true;
                return req.ActualStartTime.IsBefore(req.ActualEndTime);
            })
            .WithMessage("ActualStartTime must be before ActualEndTime.")
            .When(x => x.ActualStartTime != null && x.ActualEndTime != null);
    }
}