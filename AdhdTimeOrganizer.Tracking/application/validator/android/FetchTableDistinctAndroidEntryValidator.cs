using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.android;
using FastEndpoints;
using FluentValidation;
using Sydowwe.Framework.application.dto.request.@base.table;

namespace AdhdTimeOrganizer.Tracking.application.validator;

/// <summary>
/// The android twin of <see cref="FetchTableDistinctDesktopEntryValidator"/> — see there for why this
/// grid needed one (<c>CQ-14</c>).
/// </summary>
public class FetchTableDistinctAndroidEntryValidator
    : Validator<BaseFilterSortPaginateRequest<AndroidDistinctEntriesFilter>>
{
    public FetchTableDistinctAndroidEntryValidator()
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

            // A Regex match type now reaches Postgres as `~*` rather than a LIKE (CQ-12).
            RuleFor(x => x.Filter!.PackageName).MustBeValidRegexWhenRegexMatch(x => x.Filter!.PackageNameMatchType);
            RuleFor(x => x.Filter!.AppLabel).MustBeValidRegexWhenRegexMatch(x => x.Filter!.AppLabelMatchType);

            // Null is "All"; an undefined member is a 400 here rather than a fall-through in the endpoint (B24).
            RuleFor(x => x.Filter!.Coverage).IsInEnum();
        });
    }
}
