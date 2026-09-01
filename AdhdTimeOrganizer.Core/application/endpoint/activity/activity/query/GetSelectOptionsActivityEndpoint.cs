using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.dto.response.generic;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.Core.application.endpoint.activity.activity.query;

/// <summary>
/// Excludes archived activities. This is a picker — it feeds the leisure activity autocomplete, both
/// timer-preset dialogs and the store's shared activity list — and the one-line rule for the whole
/// feature is that <b>only pickers exclude archived activities</b>.
/// </summary>
/// <remarks>
/// There is no <c>includeArchived</c> escape hatch here on purpose: every caller of
/// <c>/activity/all-options</c> is choosing an activity to attach to something new, and none of them
/// is a filter over existing records. The filter surfaces that do need archived rows go through
/// <c>form-select-options</c>, which takes the parameter.
/// <para>
/// The exclusion comes from the <c>ActivityArchived</c> global filter, which this endpoint never
/// lifts — there is deliberately no <c>Filter</c> override doing it by hand. Such a predicate would
/// be a no-op here, and it is the exact shape <c>BaseActivityFormSelectOptionsEndpoint</c> warns
/// against: a hand-written predicate leaves the global filter still applied on the opt-in path.
/// </para>
/// </remarks>
public class GetSelectOptionsActivityEndpoint(
    DbContext appDbContext)
    : BaseGetSelectOptionsEndpoint<Activity>(appDbContext)
{
    protected override IQueryable<SelectOptionResponse> Map(IQueryable<Activity> query)
    {
        return query.Select(a => new SelectOptionResponse
        {
            Id = a.Id,
            Text = a.Name
        });
    }
}
