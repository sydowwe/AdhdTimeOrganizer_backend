using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.dto.response.generic;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.Core.application.endpoint.activity.activity.query;

public class GetSelectOptionsActivityEndpoint(
    DbContext appDbContext)
    : BaseGetSelectOptionsEndpoint<Activity>(appDbContext)
{
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
    /// </remarks>
    public override IQueryable<Activity> Filter(IQueryable<Activity> query) => query.Where(a => !a.IsArchived);

    protected override IQueryable<SelectOptionResponse> Map(IQueryable<Activity> query)
    {
        return query.Select(a => new SelectOptionResponse
        {
            Id = a.Id,
            Text = a.Name
        });
    }
}
