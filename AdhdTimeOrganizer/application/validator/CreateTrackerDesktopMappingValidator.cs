using AdhdTimeOrganizer.application.dto.request.activityTracking.desktop;
using FastEndpoints;
using FluentValidation;

namespace AdhdTimeOrganizer.application.validator;

public class CreateTrackerDesktopMappingValidator : Validator<CreateTrackerDesktopMappingRequest>
{
    public CreateTrackerDesktopMappingValidator()
    {
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

        // Exactly one target group: IsIgnored=true | ActivityId | RoleId/CategoryId
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

        // IsIgnored can only be true, never false
        RuleFor(x => x.IsIgnored)
            .Must(v => v == null || v == true)
            .WithMessage("IsIgnored can only be set to true");
    }
}
