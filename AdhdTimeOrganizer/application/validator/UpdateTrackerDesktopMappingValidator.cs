using AdhdTimeOrganizer.application.dto.request.activityTracking.desktop;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.application.validator;

public class UpdateTrackerDesktopMappingValidator : Validator<UpdateTrackerDesktopMappingRequest>
{
    public UpdateTrackerDesktopMappingValidator()
    {
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
    }
}
