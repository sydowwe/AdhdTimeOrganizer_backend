using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.desktop;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.Tracking.application.validator;

public class UpdateTrackerDesktopMappingValidator : Validator<UpdateTrackerDesktopMappingRequest>
{
    public UpdateTrackerDesktopMappingValidator()
    {
        // Column widths — an over-long pattern is a Postgres 22001 (500) without these (CQ-9).
        RuleFor(x => x.ProcessName).MaximumLength(TrackingIngestLimits.MaxDesktopMappingPatternLength);
        RuleFor(x => x.ProductName).MaximumLength(TrackingIngestLimits.MaxDesktopMappingPatternLength);
        RuleFor(x => x.WindowTitle).MaximumLength(TrackingIngestLimits.MaxDesktopMappingWindowTitleLength);

        RuleFor(x => x.ProcessNameMatchType)
            .NotNull().When(x => x.ProcessName != null)
            .WithMessage("ProcessNameMatchType is required when ProcessName is set");

        RuleFor(x => x.ProductNameMatchType)
            .NotNull().When(x => x.ProductName != null)
            .WithMessage("ProductNameMatchType is required when ProductName is set");

        RuleFor(x => x.WindowTitleMatchType)
            .NotNull().When(x => x.WindowTitle != null)
            .WithMessage("WindowTitleMatchType is required when WindowTitle is set");

        RuleFor(x => x)
            .Must(x => x.ProcessName != null || x.ProductName != null || x.WindowTitle != null)
            .WithMessage("At least one pattern (ProcessName, ProductName or WindowTitle) must be provided")
            .OverridePropertyName("Patterns");

        // CQ-10: the update DTO carries the target group now, so the same two rules the create validator
        // has always had apply here. Omitting them was harmless only while update could not write a
        // target; with the members present, a body naming none of them clears all four columns and hits
        // the check constraint as a 500.
        this.AddMappingTargetRules();
    }
}