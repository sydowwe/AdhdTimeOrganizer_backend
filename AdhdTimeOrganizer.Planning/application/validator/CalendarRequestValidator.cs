using AdhdTimeOrganizer.Core.application.validator;
using AdhdTimeOrganizer.Planning.application.dto.filter;
using AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner;
using AdhdTimeOrganizer.Planning.application.helper;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.Planning.application.validator;

public class CalendarRequestValidator : Validator<CalendarRequest>
{
    public CalendarRequestValidator()
    {
        RuleFor(x => x.WakeUpTime).ApplyTimeOfDayRules();
        RuleFor(x => x.BedTime).ApplyTimeOfDayRules();

        RuleFor(x => x.Label)
            .MaximumLength(100)
            .When(x => x.Label != null);

        RuleFor(x => x.Weather)
            .MaximumLength(100)
            .When(x => x.Weather != null);

        RuleFor(x => x.Notes)
            .MaximumLength(1000)
            .When(x => x.Notes != null);
    }
}

/// <summary>
/// The range read behind the month grid. It is bounded for the same reason the batched writes are — the cost
/// is picked by the caller — and by the same ceiling as its sibling <c>CalendarTaskSummariesValidator</c>,
/// which reads the same days with the same two per-row subqueries (<c>CalendarResponse.Projection</c> counts
/// a calendar's total and completed tasks). Unbounded, a `1900 → 2100` filter scans the user's whole calendar
/// table and runs those two correlated subqueries on every row of it.
/// </summary>
public class CalendarFilterValidator : Validator<CalendarFilter>
{
    public CalendarFilterValidator()
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