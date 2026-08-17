using AdhdTimeOrganizer.Core.application.dto.@enum;
using FluentValidation;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.Core.application.dto.dto;

public record DateRangeDto
{
    public required DateOnly Date { get; init; }
    public required ActivityDateRangeType RangeType { get; init; }

    public DateOnly? EndDate { get; init; }

    public (DateOnly From, DateOnly To) ToDateRange()
    {
        var (from, to) = RangeType switch
        {
            ActivityDateRangeType.ThreeDays => (Date.AddDays(-3), Date),
            ActivityDateRangeType.Week => (Date.AddDays(-7), Date),
            ActivityDateRangeType.TwoWeeks => (Date.AddDays(-14), Date),
            ActivityDateRangeType.Month => (Date.AddMonths(-1), Date),
            ActivityDateRangeType.ThreeMonths => (Date.AddMonths(-3), Date),
            ActivityDateRangeType.Year => (Date.AddYears(-1), Date),
            _ => (Date, Date)
        };


        if (to <= from)
            to = to.AddDays(1);

        return (from, to);
    }

    /// <summary>
    /// <see cref="ToDateRange"/> resolved to the half-open UTC instant range the days actually cover on the
    /// clocks of <paramref name="timeZone"/> — the requesting user's own zone (<c>User.Timezone</c>).
    ///
    /// <para>Days are the user's days. Midnight in Bratislava is 23:00 the previous day in UTC, so anchoring
    /// the range at UTC midnight (which is what every caller of <see cref="ToDateRange"/> used to do) credits
    /// the user's last hour of every evening to the following day — the same class of error
    /// <c>PlannerStreakReader</c> already avoids by resolving <c>today</c> in the user's zone.</para>
    ///
    /// <para>The upper bound is exclusive and covers the whole of <c>To</c>: the range ends at the user's
    /// midnight opening the day <i>after</i> it.</para>
    /// </summary>
    public (DateTime From, DateTime To) ToUtcRange(TimeZoneInfo timeZone)
    {
        var (fromDate, toDate) = ToDateRange();

        return (
            WallClockZone.ToUtc(fromDate, TimeOnly.MinValue, timeZone),
            WallClockZone.ToUtc(toDate.AddDays(1), TimeOnly.MinValue, timeZone));
    }

    public static void ApplyValidationRules<T>(AbstractValidator<T> validator, Func<T, DateRangeDto> selector)
    {
        validator.RuleFor(x => selector(x).Date).NotEmpty();
        validator.RuleFor(x => selector(x).RangeType).IsInEnum();
        validator.RuleFor(x => selector(x).EndDate)
            .Must((root, endDate) => endDate >= selector(root).Date)
            .When(x => selector(x).EndDate.HasValue)
            .WithMessage("EndDate must be greater than or equal to Date");
    }
}