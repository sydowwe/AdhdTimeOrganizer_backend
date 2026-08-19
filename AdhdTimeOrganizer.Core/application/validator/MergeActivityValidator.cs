using AdhdTimeOrganizer.Core.application.dto.request.activity;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.Core.application.validator;

/// <summary>
/// The two shapes of merge request that mean the client is broken rather than the user is in an odd
/// state. Both are 400, and both carry a message, because the user reaches them after confirming an
/// irreversible action — "something went wrong" is a bad answer there.
/// </summary>
public class MergeActivityValidator : Validator<MergeActivityRequest>
{
    public MergeActivityValidator()
    {
        RuleFor(x => x.SurvivorId)
            .GreaterThan(0)
            .WithMessage("A survivor activity must be chosen.");

        RuleFor(x => x.MergedIds)
            .NotEmpty()
            .WithMessage("Choose at least one activity to merge into the survivor.");

        // The dialog strips the survivor from the list, so this only fires on a client bug. Rejecting it
        // rather than silently ignoring it matters: folding an activity into itself would repoint its own
        // rows onto itself and then delete it, taking every one of them with it through the cascade.
        RuleFor(x => x)
            .Must(x => !x.MergedIds.Contains(x.SurvivorId))
            .WithName(nameof(MergeActivityRequest.MergedIds))
            .WithMessage("The survivor activity cannot also be one of the activities being merged away.");
    }
}
