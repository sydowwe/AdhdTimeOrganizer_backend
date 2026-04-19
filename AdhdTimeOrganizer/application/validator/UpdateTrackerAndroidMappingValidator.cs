using AdhdTimeOrganizer.application.dto.request.activityTracking.android;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.application.validator;

public class UpdateTrackerAndroidMappingValidator : Validator<UpdateTrackerAndroidMappingRequest>
{
    public UpdateTrackerAndroidMappingValidator()
    {
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
    }
}
