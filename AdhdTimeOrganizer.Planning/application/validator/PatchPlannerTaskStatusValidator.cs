using AdhdTimeOrganizer.Core.application.validator;
using AdhdTimeOrganizer.Planning.application.dto;
using AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
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

        // Measured after the trim ApplyStatus applies, so the cap matches what the 500-char column receives.
        RuleFor(x => x.SkipReason)
            .Must(r => r!.Trim().Length <= 500)
            .WithMessage("SkipReason must be 500 characters or fewer.")
            .When(x => !string.IsNullOrWhiteSpace(x.SkipReason));

        // Rejected rather than dropped: ApplyStatus clears the reason for every status but Cancelled, so
        // accepting it here would be accepting a value that is never stored. Null/blank stays fine with any
        // status — the client always sends skipReason: null beside a non-cancelled status.
        RuleFor(x => x.SkipReason)
            .Must((req, _) => req.Status == PlannerTaskStatus.Cancelled)
            .WithMessage("SkipReason is only allowed when Status is Cancelled.")
            .When(x => !string.IsNullOrWhiteSpace(x.SkipReason));

        RuleFor(x => x.SkipReasonKey)
            .Must(k => SkipReasonKeyTokens.TryParse(k, out _))
            .WithMessage($"SkipReasonKey must be one of: {SkipReasonKeyTokens.NoTime}, {SkipReasonKeyTokens.NotRelevant}, {SkipReasonKeyTokens.NoEnergy}.")
            .When(x => x.SkipReasonKey != null);

        RuleFor(x => x.SkipReasonKey)
            .Must((req, _) => req.Status == PlannerTaskStatus.Cancelled)
            .WithMessage("SkipReasonKey is only allowed when Status is Cancelled.")
            .When(x => x.SkipReasonKey != null);

        // One reason per skip. Picking which of the two to keep would silently drop the other.
        RuleFor(x => x)
            .Must(x => x.SkipReasonKey == null || string.IsNullOrWhiteSpace(x.SkipReason))
            .WithName(nameof(PatchPlannerTaskStatusRequest.SkipReasonKey))
            .WithMessage("Send either SkipReasonKey or SkipReason, not both.");
    }
}