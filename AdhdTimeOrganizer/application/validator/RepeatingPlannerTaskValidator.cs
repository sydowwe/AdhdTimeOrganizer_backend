using AdhdTimeOrganizer.application.dto.request.taskPlanner;
using AdhdTimeOrganizer.domain.model.@enum;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.application.validator;

public class RepeatingPlannerTaskValidator : Validator<RepeatingPlannerTaskRequest>
{
    private static readonly string[] ValidDayNames =
        ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];

    private static readonly string[] ValidDayTypes =
        ["Workday", "Weekend", "Vacation", "SickDay", "Special"];

    public RepeatingPlannerTaskValidator()
    {
        RuleFor(x => x.ActivityId)
            .GreaterThan(0);

        RuleFor(x => x.ImportanceId)
            .NotNull()
            .GreaterThan(0);

        RuleFor(x => x.StartTime)
            .Must(t => t.Hours is >= 0 and <= 23 && t.Minutes is >= 0 and <= 59)
            .WithMessage("StartTime hours must be 0–23 and minutes 0–59.");

        RuleFor(x => x.EndTime)
            .Must(t => t.Hours is >= 0 and <= 23 && t.Minutes is >= 0 and <= 59)
            .WithMessage("EndTime hours must be 0–23 and minutes 0–59.");

        RuleFor(x => x.Location)
            .MaximumLength(200)
            .When(x => x.Location != null);

        RuleFor(x => x.Notes)
            .MaximumLength(1000)
            .When(x => x.Notes != null);

        // DayOfWeek: ScheduledDays must be non-empty with valid day names
        RuleFor(x => x.ScheduledDays)
            .NotEmpty()
            .WithMessage("ScheduledDays must contain at least one day.")
            .Must(days => days.All(d => ValidDayNames.Contains(d)))
            .WithMessage($"ScheduledDays must only contain valid day names: {string.Join(", ", ValidDayNames)}.")
            .When(x => x.RecurrenceType == RecurrenceType.DayOfWeek);

        // DayOfMonth: ScheduledDates must be non-empty with values 1–31
        RuleFor(x => x.ScheduledDates)
            .NotEmpty()
            .WithMessage("ScheduledDates must contain at least one day-of-month value.")
            .Must(dates => dates.All(d => d is >= 1 and <= 31))
            .WithMessage("ScheduledDates values must be between 1 and 31.")
            .When(x => x.RecurrenceType == RecurrenceType.DayOfMonth);

        // DateRange: both ActiveFromDate and ActiveToDate must be set, and FromDate <= ToDate
        RuleFor(x => x.ActiveFromDate)
            .NotNull()
            .WithMessage("ActiveFromDate is required for DateRange recurrence.")
            .When(x => x.RecurrenceType == RecurrenceType.DateRange);

        RuleFor(x => x.ActiveToDate)
            .NotNull()
            .WithMessage("ActiveToDate is required for DateRange recurrence.")
            .When(x => x.RecurrenceType == RecurrenceType.DateRange);

        RuleFor(x => x)
            .Must(x => x.ActiveFromDate!.Value <= x.ActiveToDate!.Value)
            .WithMessage("ActiveFromDate must be on or before ActiveToDate.")
            .When(x => x.RecurrenceType == RecurrenceType.DateRange
                        && x.ActiveFromDate.HasValue && x.ActiveToDate.HasValue);

        // DayType: ScheduledForDayTypes must be non-empty with valid DayType names
        RuleFor(x => x.ScheduledForDayTypes)
            .NotEmpty()
            .WithMessage("ScheduledForDayTypes must contain at least one day type.")
            .Must(types => types.All(t => ValidDayTypes.Contains(t)))
            .WithMessage($"ScheduledForDayTypes must only contain valid day types: {string.Join(", ", ValidDayTypes)}.")
            .When(x => x.RecurrenceType == RecurrenceType.DayType);
    }
}
