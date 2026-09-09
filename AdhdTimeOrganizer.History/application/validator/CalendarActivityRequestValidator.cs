using AdhdTimeOrganizer.History.application.dto.request.calendar;
using AdhdTimeOrganizer.Core.application.dto.dto;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.History.application.validator;

/// <summary>
/// The calendar dashboard's range rules. It is the one range-taking request in this slice that does not
/// carry a <see cref="DateRangeDto"/>, so it cannot reuse
/// <see cref="DateRangeDto.ApplyValidationRules{T}"/> and states the same three constraints itself —
/// ordered ends, a span within <see cref="DateRangeDto.MaxCustomRangeDays"/>, and a bounded
/// <c>TopN</c>. Without them the endpoint loads every <c>ActivityHistory</c> row the caller owns for an
/// arbitrary span, with <c>Activity</c> and <c>ActivityRole</c> joined, into memory.
///
/// <para>Concrete on purpose, like the <c>summary/</c> validators beside it: FastEndpoints resolves a
/// validator by the request's <b>exact</b> type, so an abstract or open-generic one is never discovered
/// and the endpoint keeps answering 200 for a range nobody should be able to ask for.</para>
/// </summary>
public class CalendarActivityRequestValidator : Validator<CalendarActivityRequest>
{
    /// <summary>
    /// The calendar renders at most a handful of role chips per day cell; ten is well past what fits and
    /// keeps the per-day <c>Take</c> from being caller-controlled.
    /// </summary>
    public const int MaxTopN = 10;

    public CalendarActivityRequestValidator()
    {
        RuleFor(x => x.StartDate)
            .NotEmpty();

        RuleFor(x => x.EndDate)
            .NotEmpty()
            .GreaterThanOrEqualTo(x => x.StartDate)
            .WithMessage("EndDate must be greater than or equal to StartDate")
            // Inclusive day count, so the two ends may be MaxCustomRangeDays - 1 apart. Short-circuited on
            // the rule above: an inverted range is already reported as inverted, and a negative span
            // reported as "too long" would send the client looking in the wrong place.
            .Must((req, endDate) => endDate < req.StartDate
                                    || endDate.DayNumber - req.StartDate.DayNumber
                                    < DateRangeDto.MaxCustomRangeDays)
            .WithMessage($"A calendar range may span at most {DateRangeDto.MaxCustomRangeDays} days");

        RuleFor(x => x.TopN)
            .InclusiveBetween(1, MaxTopN);
    }
}
