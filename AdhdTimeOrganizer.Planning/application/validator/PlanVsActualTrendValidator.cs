using AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner;
using AdhdTimeOrganizer.Planning.application.helper;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.Planning.application.validator;

/// <summary>
/// The same two rules <c>CalendarTaskSummariesValidator</c> applies to its window, and for the same reason:
/// the range is what bounds the scan, so an unbounded one is the only way this read can get expensive.
/// A range entirely in the future is <b>not</b> rejected here — it is well-formed and answers "nothing to
/// say", which the endpoint returns as a zeroed aggregate.
/// </summary>
public class PlanVsActualTrendValidator : Validator<PlanVsActualTrendRequest>
{
    public PlanVsActualTrendValidator()
    {
        RuleFor(x => x)
            .Must(x => x.Until >= x.From)
            .WithMessage("Until must be on or after From.");

        RuleFor(x => x)
            .Must(x => x.Until.DayNumber - x.From.DayNumber + 1 <= PlannerBatchLimits.MaxSummaryRangeDays)
            .WithMessage($"The range may not exceed {PlannerBatchLimits.MaxSummaryRangeDays} days.")
            .When(x => x.Until >= x.From);
    }
}
