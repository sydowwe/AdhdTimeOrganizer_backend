using AdhdTimeOrganizer.Routines.application.dto.request.todoList;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.Routines.application.validator;

/// <summary>
/// Shape only. Which week-start is "the current week" depends on the caller's <c>FirstDayOfWeek</c> and on the
/// time zone their device is in — neither is decidable here — so this checks that the value is a date at all
/// and that it is not implausibly far from today, rather than trying to police which day of the week it is.
/// </summary>
public class UserRoutineSettingsValidator : Validator<UserRoutineSettingsRequest>
{
    /// <summary>Wide enough to cover any time-zone skew and a client clock that is days out; narrow enough that a parsed-wrong year is a 400.</summary>
    private const int MaxDaysAhead = 30;

    private const int MaxDaysBehind = 366 * 5;

    public UserRoutineSettingsValidator()
    {
        RuleFor(x => x.RoutineReviewDismissedForWeekStart)
            .Must(x => string.IsNullOrWhiteSpace(x) || UserRoutineSettingsRequest.TryParse(x, out _))
            .WithMessage("RoutineReviewDismissedForWeekStart must be an ISO-8601 date or instant, or null.")
            .Must(BeWithinAPlausibleRange)
            .WithMessage($"RoutineReviewDismissedForWeekStart must be within {MaxDaysBehind} days back and {MaxDaysAhead} days ahead of today.");
    }

    private static bool BeWithinAPlausibleRange(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !UserRoutineSettingsRequest.TryParse(value, out var weekStart))
            return true; // the parse rule above owns that failure; don't report it twice

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return weekStart >= today.AddDays(-MaxDaysBehind) && weekStart <= today.AddDays(MaxDaysAhead);
    }
}
