using AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner;
using AdhdTimeOrganizer.Planning.domain.service;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.Planning.application.validator;

/// <summary>
/// The shape of an apply. Whether the decisions cover exactly the preview's <c>wontFit</c> needs the day, so the
/// endpoint checks that after recomputing the plan.
/// </summary>
public class ApplyDayReplanValidator : Validator<ApplyDayReplanRequest>
{
    public ApplyDayReplanValidator()
    {
        RuleFor(x => x.PreviewToken).NotEmpty();

        RuleFor(x => x.WontFit).NotNull();

        RuleForEach(x => x.WontFit).ChildRules(decision =>
        {
            decision.RuleFor(d => d.TaskId).GreaterThan(0);
            decision.RuleFor(d => d.Resolution).IsInEnum();
        });

        // One decision per task: two for the same task would leave which one wins to the order of a list.
        RuleFor(x => x.WontFit)
            .Must(decisions => decisions.Select(d => d.TaskId).Distinct().Count() == decisions.Count)
            .When(x => x.WontFit != null)
            .WithMessage("Each task may be decided once.");
    }
}

public class NextFreeSlotValidator : Validator<NextFreeSlotRequest>
{
    public NextFreeSlotValidator()
    {
        RuleFor(x => x.Minutes).InclusiveBetween(1, DayPacking.LastEndMinute);

        RuleFor(x => x.NotBeforeMinute!.Value)
            .InclusiveBetween(0, DayPacking.LastEndMinute)
            .When(x => x.NotBeforeMinute.HasValue)
            .OverridePropertyName(nameof(NextFreeSlotRequest.NotBeforeMinute));

        RuleFor(x => x.ExcludeTaskId!.Value)
            .GreaterThan(0)
            .When(x => x.ExcludeTaskId.HasValue)
            .OverridePropertyName(nameof(NextFreeSlotRequest.ExcludeTaskId));
    }
}
