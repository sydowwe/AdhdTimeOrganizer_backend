using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.Tracking.application.validator;

public class FocusMetricsValidator : Validator<FocusMetricsRequest>
{
    /// <summary>
    /// An hour. Past that the "longest unbroken block" measure stops meaning anything — every block
    /// swallows every interruption and the whole day reads as one — and the client's own constant is
    /// two minutes, so this is a sanity bound on a hand-crafted request rather than a real limit.
    /// </summary>
    public const int MaxFocusGapSeconds = 3600;

    public FocusMetricsValidator()
    {
        // Deliberately no ApplySingleDayRule: unlike the timeline, a range is the point.
        this.ApplyDateRangeRules();

        RuleFor(x => x.Baseline)
            .IsInEnum()
            .When(x => x.Baseline.HasValue);

        RuleFor(x => x.FocusGapSeconds)
            .InclusiveBetween(0, MaxFocusGapSeconds)
            .WithMessage($"FocusGapSeconds must be between 0 and {MaxFocusGapSeconds}");
    }
}
