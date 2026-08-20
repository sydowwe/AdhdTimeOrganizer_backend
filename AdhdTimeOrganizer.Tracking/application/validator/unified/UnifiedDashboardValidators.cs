using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.unified;
using AdhdTimeOrganizer.Tracking.domain.helper.unified;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.Tracking.application.validator.unified;

/// <summary>
/// The <c>sources</c> rule, shared by all six unified dashboards — the same reasoning as
/// <see cref="DashboardDateRangeRules"/>: a rule applied to five of six and forgotten on the sixth is
/// an endpoint answering a question nobody asked, and nothing about it looks wrong.
/// </summary>
public static class UnifiedSourceRules
{
    public static void ApplySourceRules<T>(this AbstractValidator<T> validator)
        where T : IUnifiedSourceSelection
    {
        // An empty selection asks for a picture of nothing. Defaulting it to all three would answer a
        // question the client never asks — its own filter refuses to turn off the last source — and
        // would hide the bug that produced it.
        validator.RuleFor(x => x.Sources)
            .NotEmpty()
            .WithMessage("At least one source must be selected");

        // Loudly, not silently: a typo in a shared link should fail rather than quietly narrow the
        // picture to whatever survived parsing.
        validator.RuleFor(x => x.Sources)
            .Must(sources => sources.All(source => TrackingSourceNames.TryParse(source, out _)))
            .WithMessage(TrackingSourceNames.AllowedMessage)
            .When(x => x.Sources.Count > 0);
    }
}

public class UnifiedSourcesValidator : Validator<UnifiedDashboardRequest>
{
    public UnifiedSourcesValidator()
    {
        this.ApplyDateRangeRules();
        this.ApplySourceRules();
    }
}

public class UnifiedPieChartValidator : Validator<UnifiedPieChartRequest>
{
    public UnifiedPieChartValidator()
    {
        this.ApplyDateRangeRules();
        this.ApplySourceRules();
        RuleFor(x => x.MinPercent).InclusiveBetween(0.1, 50.0).When(x => x.MinPercent.HasValue);
    }
}

public class UnifiedSummaryCardsValidator : Validator<UnifiedSummaryCardsRequest>
{
    public UnifiedSummaryCardsValidator()
    {
        this.ApplyDateRangeRules();
        this.ApplySourceRules();
        RuleFor(x => x.TopN).InclusiveBetween(1, 50).When(x => x.TopN.HasValue);
        RuleFor(x => x.Baseline).IsInEnum();
    }
}

public class UnifiedStackedBarsValidator : Validator<UnifiedStackedBarsRequest>
{
    public UnifiedStackedBarsValidator()
    {
        this.ApplyDateRangeRules();
        this.ApplySourceRules();
        RuleFor(x => x.WindowMinutes)
            .Must(DashboardDateRangeRules.AllowedWindowMinutes.Contains)
            .WithMessage(DashboardDateRangeRules.WindowMinutesMessage);
    }
}

public class UnifiedTimelineValidator : Validator<UnifiedTimelineRequest>
{
    public UnifiedTimelineValidator()
    {
        this.ApplyDateRangeRules();
        this.ApplySingleDayRule();
        this.ApplySourceRules();
    }
}

public class UnifiedFocusMetricsValidator : Validator<UnifiedFocusMetricsRequest>
{
    public UnifiedFocusMetricsValidator()
    {
        // Deliberately no ApplySingleDayRule: as per source, a range is half of why this one exists.
        this.ApplyDateRangeRules();
        this.ApplySourceRules();

        RuleFor(x => x.Baseline).IsInEnum().When(x => x.Baseline.HasValue);

        RuleFor(x => x.FocusGapSeconds)
            .InclusiveBetween(0, FocusMetricsValidator.MaxFocusGapSeconds)
            .WithMessage($"FocusGapSeconds must be between 0 and {FocusMetricsValidator.MaxFocusGapSeconds}");
    }
}
