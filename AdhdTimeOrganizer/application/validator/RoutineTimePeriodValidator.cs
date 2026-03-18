using AdhdTimeOrganizer.application.dto.request.toDoList;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.application.validator;

public class RoutineTimePeriodValidator : Validator<RoutineTimePeriodRequest>
{
    private static bool IsWeeklyAligned(int lengthInDays) =>
        lengthInDays <= 7 || lengthInDays % 7 == 0;

    public RoutineTimePeriodValidator()
    {
        RuleFor(x => x.LengthInDays)
            .GreaterThanOrEqualTo(1);

        RuleFor(x => x.StreakThreshold)
            .InclusiveBetween(1, 100);

        RuleFor(x => x.StreakGraceDays)
            .GreaterThanOrEqualTo(0)
            .Must((req, v) => v < req.LengthInDays)
            .WithMessage("StreakGraceDays must be less than LengthInDays.");

        // Weekly-aligned: 0 = rolling, 1–7 = Mon–Sun
        RuleFor(x => x.ResetAnchorDay)
            .InclusiveBetween(0, 7)
            .When(x => IsWeeklyAligned(x.LengthInDays))
            .WithMessage("For weekly-aligned periods ResetAnchorDay must be 0 (rolling) or 1–7 (Mon–Sun).");

        // Day-of-month: 0 = rolling, 1–30
        RuleFor(x => x.ResetAnchorDay)
            .InclusiveBetween(0, 30)
            .When(x => !IsWeeklyAligned(x.LengthInDays))
            .WithMessage("For non-weekly periods ResetAnchorDay must be 0 (rolling) or 1–30 (day of month).");
    }
}
