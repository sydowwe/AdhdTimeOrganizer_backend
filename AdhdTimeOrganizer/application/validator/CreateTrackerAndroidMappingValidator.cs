using AdhdTimeOrganizer.application.dto.request.activityTracking.android;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.application.validator;

public class CreateTrackerAndroidMappingValidator : Validator<CreateTrackerAndroidMappingRequest>
{
    public CreateTrackerAndroidMappingValidator()
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

        RuleFor(x => x)
            .Must(x =>
            {
                var isIgnoredSet = x.IsIgnored == true;
                var activitySet = x.ActivityId != null;
                var roleOrCategorySet = x.RoleId != null || x.CategoryId != null;
                var targetsSet = new[] { isIgnoredSet, activitySet, roleOrCategorySet }.Count(v => v);
                return targetsSet == 1;
            })
            .WithMessage("Exactly one target must be set: IsIgnored=true, ActivityId, or RoleId/CategoryId (not multiple)")
            .OverridePropertyName("Target");

        RuleFor(x => x.IsIgnored)
            .Must(v => v == null || v == true)
            .WithMessage("IsIgnored can only be set to true");
    }
}
