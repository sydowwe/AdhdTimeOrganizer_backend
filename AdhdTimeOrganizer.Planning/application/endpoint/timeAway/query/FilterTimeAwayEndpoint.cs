using AdhdTimeOrganizer.Planning.application.dto.filter;
using AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.Planning.application.validator;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using FastEndpoints;
using Sydowwe.Framework.application.dto.request.generic;
using Sydowwe.Framework.application.endpoint.@base.read.pageFilterSort;

namespace AdhdTimeOrganizer.Planning.application.endpoint.activityPlanning.timeAway.query;

/// <summary>
/// <c>POST /time-away/filter</c> — the user's stretches touching a date range, oldest first; with no range, all of
/// them (the <i>Time away</i> list). The base's <c>ApplyUserScoping</c> is left a no-op on purpose:
/// <see cref="TimeAway"/> is a portal <c>IEntityWithUser</c>, so the host's global filter is what scopes this read.
/// </summary>
public class FilterTimeAwayEndpoint(DbContext dbContext)
    : BaseFilterEndpoint<TimeAway, TimeAwayResponse, TimeAwayFilter>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Validator<TimeAwayFilterValidator>();
    }

    protected override IQueryable<TimeAway> ApplyCustomFiltering(IQueryable<TimeAway> query, TimeAwayFilter filter)
    {
        if (filter.Until is { } until)
            query = query.Where(t => t.StartDate <= until);
        if (filter.From is { } from)
            query = query.Where(t => t.EndDate >= from);
        return query;
    }

    public override SortByRequest[] AlwaysSortBy =>
    [
        new() { Key = "StartDate", IsDesc = false }
    ];
}
