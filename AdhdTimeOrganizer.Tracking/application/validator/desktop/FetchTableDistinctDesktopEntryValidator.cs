using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.desktop;
using FastEndpoints;
using FluentValidation;
using Sydowwe.Framework.application.dto.request.@base.table;

namespace AdhdTimeOrganizer.Tracking.application.validator;

/// <summary>
/// The distinct-entries grid had no validator at all (<c>CQ-14</c>), unlike the two mapping grids it
/// sits beside in the settings UI. <c>ItemsPerPage = 0</c> made the endpoint's
/// <c>(int)Math.Ceiling(itemsCount / 0.0)</c> a garbage page count, and a large one an unbounded page
/// over a <c>GROUP BY</c> of the whole partitioned ledger.
/// </summary>
public class FetchTableDistinctDesktopEntryValidator
    : Validator<BaseFilterSortPaginateRequest<TrackerDesktopDistinctEntriesFilter>>
{
    public FetchTableDistinctDesktopEntryValidator()
    {
        RuleFor(x => x.ItemsPerPage).InclusiveBetween(1, 200);
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);

        // The handler applies a filter only when both the value and its match type are set, so a value
        // sent without one is silently ignored rather than filtered on. Same rules as the mapping grids.
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
