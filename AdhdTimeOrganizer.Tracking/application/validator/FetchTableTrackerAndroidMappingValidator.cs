using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.android;
using FastEndpoints;
using FluentValidation;
using Sydowwe.Framework.application.dto.request.@base.table;

namespace AdhdTimeOrganizer.Tracking.application.validator;

public class FetchTableTrackerAndroidMappingValidator : Validator<BaseFilterSortPaginateRequest<TrackerAndroidMappingFilter>>
{
    public FetchTableTrackerAndroidMappingValidator()
    {
        RuleFor(x => x.ItemsPerPage).InclusiveBetween(1, 200);
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);

        When(x => x.UseFilter && x.Filter != null, () =>
        {
            RuleFor(x => x.Filter!.PackageNameMatchType)
                .NotNull().When(x => x.Filter!.PackageName != null)
                .WithMessage("PackageNameMatchType is required when PackageName is set");

            RuleFor(x => x.Filter!.AppLabelMatchType)
                .NotNull().When(x => x.Filter!.AppLabel != null)
                .WithMessage("AppLabelMatchType is required when AppLabel is set");
        });
    }
}