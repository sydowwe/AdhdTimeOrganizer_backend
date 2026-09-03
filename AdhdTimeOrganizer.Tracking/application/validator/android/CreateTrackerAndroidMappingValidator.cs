using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.android;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.Tracking.application.validator;

public class CreateTrackerAndroidMappingValidator : Validator<CreateTrackerAndroidMappingRequest>
{
    public CreateTrackerAndroidMappingValidator()
    {
        // Column widths — an over-long pattern is a Postgres 22001 (500) without these (CQ-9).
        RuleFor(x => x.PackageName).MaximumLength(TrackingIngestLimits.MaxAndroidMappingPatternLength);
        RuleFor(x => x.AppLabel).MaximumLength(TrackingIngestLimits.MaxAndroidMappingPatternLength);

        RuleFor(x => x.PackageNameMatchType)
            .NotNull().When(x => x.PackageName != null)
            .WithMessage("PackageNameMatchType is required when PackageName is set");

        RuleFor(x => x.AppLabelMatchType)
            .NotNull().When(x => x.AppLabel != null)
            .WithMessage("AppLabelMatchType is required when AppLabel is set");

        RuleFor(x => x)
            .Must(x => x.PackageName != null || x.AppLabel != null)
            .WithMessage("At least one pattern (PackageName or AppLabel) must be provided")
            .OverridePropertyName("Patterns");

        // The target group's two rules, shared with the update validator and the desktop pair (CQ-10).
        this.AddMappingTargetRules();
    }
}