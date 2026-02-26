using AdhdTimeOrganizer.application.dto.@enum;
using FluentValidation;

namespace AdhdTimeOrganizer.application.dto.dto;

public record DateRangeDto
{
    public required DateOnly Date { get; init; }
    public required ActivityDateRangeType RangeType { get; init; }

    public DateOnly? EndDate { get; init; }

    public (DateOnly From, DateOnly To) ToDateRange()
    {
        var (from, to) = RangeType switch
        {
            ActivityDateRangeType.Day => (Date, Date),
            ActivityDateRangeType.ThreeDays => (Date.AddDays(-3), Date),
            ActivityDateRangeType.Week => (Date.AddDays(-7), Date),
            ActivityDateRangeType.TwoWeeks => (Date.AddDays(-14), Date),
            ActivityDateRangeType.Month => (Date.AddMonths(-1), Date),
            ActivityDateRangeType.ThreeMonths => (Date.AddMonths(-3), Date),
            ActivityDateRangeType.Year => (Date.AddYears(-1), Date),
            _ => (Date, Date)
        };


        if (to <= from)
        {
            to = to.AddDays(1);
        }

        return (from, to);
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