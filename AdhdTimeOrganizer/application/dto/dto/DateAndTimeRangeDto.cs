using AdhdTimeOrganizer.application.dto.@enum;
using FluentValidation;

namespace AdhdTimeOrganizer.application.dto.dto;

public record DateAndTimeRangeDto
{
    public required DateOnly Date { get; init; }
    public required ActivityDateRangeType RangeType { get; init; }

    public DateOnly? EndDate { get; init; }
    public TimeDto From { get; init; } = new(0, 0);
    public TimeDto To { get; init; } = new(23, 59);

    public (DateTime From, DateTime To) ToDateTimeRange()
    {
        var from = Date.ToDateTime(new TimeOnly(From.Hours, From.Minutes))
            .ToUniversalTime();

        var toDate = RangeType switch
        {
            ActivityDateRangeType.OneDay => Date,
            ActivityDateRangeType.OneWeek => Date.AddDays(7),
            ActivityDateRangeType.OneMonth => Date.AddMonths(1),
            ActivityDateRangeType.ThisMonth => new DateOnly(Date.Year, Date.Month, DateTime.DaysInMonth(Date.Year, Date.Month)),
            ActivityDateRangeType.ThisYear => new DateOnly(Date.Year, 12, 31),
            ActivityDateRangeType.CustomRange => EndDate ?? Date,
            _ => Date
        };

        var to = toDate.ToDateTime(new TimeOnly(To.Hours, To.Minutes))
            .ToUniversalTime();

        if (to <= from)
        {
            to = to.AddDays(1);
        }

        return (from, to);
    }

    public static void ApplyValidationRules<T>(AbstractValidator<T> validator, Func<T, DateAndTimeRangeDto> selector)
    {
        validator.RuleFor(x => selector(x).Date).NotEmpty();
        validator.RuleFor(x => selector(x).RangeType).IsInEnum();
        validator.RuleFor(x => selector(x).From.Hours).InclusiveBetween(0, 23);
        validator.RuleFor(x => selector(x).From.Minutes).InclusiveBetween(0, 59);
        validator.RuleFor(x => selector(x).To.Hours).InclusiveBetween(0, 23);
        validator.RuleFor(x => selector(x).To.Minutes).InclusiveBetween(0, 59);
        validator.RuleFor(x => selector(x).EndDate)
            .NotNull()
            .When(x => selector(x).RangeType == ActivityDateRangeType.CustomRange)
            .WithMessage("EndDate is required when RangeType is CustomRange");
        validator.RuleFor(x => selector(x).EndDate)
            .Must((root, endDate) => endDate >= selector(root).Date)
            .When(x => selector(x).EndDate.HasValue)
            .WithMessage("EndDate must be greater than or equal to Date");
    }
}