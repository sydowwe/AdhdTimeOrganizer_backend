using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.desktop;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.Tracking.application.validator;

public class CreateTrackerDesktopMappingValidator : Validator<CreateTrackerDesktopMappingRequest>
{
    public CreateTrackerDesktopMappingValidator()
    {
        // Column widths — an over-long pattern is a Postgres 22001 (500) without these (CQ-9).
        RuleFor(x => x.ProcessName).MaximumLength(TrackingIngestLimits.MaxDesktopMappingPatternLength);
        RuleFor(x => x.ProductName).MaximumLength(TrackingIngestLimits.MaxDesktopMappingPatternLength);
        RuleFor(x => x.WindowTitle).MaximumLength(TrackingIngestLimits.MaxDesktopMappingWindowTitleLength);

        // Pattern field rules: if value is set, MatchType must be set
        RuleFor(x => x.ProcessNameMatchType)
            .NotNull().When(x => x.ProcessName != null)
            .WithMessage("ProcessNameMatchType is required when ProcessName is set");

        RuleFor(x => x.ProductNameMatchType)
            .NotNull().When(x => x.ProductName != null)
            .WithMessage("ProductNameMatchType is required when ProductName is set");

        RuleFor(x => x.WindowTitleMatchType)
            .NotNull().When(x => x.WindowTitle != null)
            .WithMessage("WindowTitleMatchType is required when WindowTitle is set");

        // At least one pattern must be provided
        RuleFor(x => x)
            .Must(x => x.ProcessName != null || x.ProductName != null || x.WindowTitle != null)
            .WithMessage("At least one pattern (ProcessName, ProductName or WindowTitle) must be provided")
            .OverridePropertyName("Patterns");

        // The target group's two rules, shared with the update validator and the android pair (CQ-10).
        this.AddMappingTargetRules();
    }
}