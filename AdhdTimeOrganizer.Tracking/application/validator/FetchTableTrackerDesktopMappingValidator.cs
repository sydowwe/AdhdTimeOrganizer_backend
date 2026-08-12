using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.desktop;
using FastEndpoints;
using FluentValidation;
using Sydowwe.Framework.application.dto.request.@base.table;

namespace AdhdTimeOrganizer.Tracking.application.validator;

public class FetchTableTrackerDesktopMappingValidator : Validator<BaseFilterSortPaginateRequest<TrackerDesktopMappingFilter>>
{
    public FetchTableTrackerDesktopMappingValidator()
    {
        RuleFor(x => x.ItemsPerPage).InclusiveBetween(1, 200);
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);

        When(x => x.UseFilter && x.Filter != null, () =>
        {
            RuleFor(x => x.Filter!.ProcessNameMatchType)
                .NotNull().When(x => x.Filter!.ProcessName != null)
                .WithMessage("ProcessNameMatchType is required when ProcessName is set");

            RuleFor(x => x.Filter!.ProductNameMatchType)
                .NotNull().When(x => x.Filter!.ProductName != null)
                .WithMessage("ProductNameMatchType is required when ProductName is set");

            RuleFor(x => x.Filter!.WindowTitleMatchType)
                .NotNull().When(x => x.Filter!.WindowTitle != null)
                .WithMessage("WindowTitleMatchType is required when WindowTitle is set");
        });
    }
}