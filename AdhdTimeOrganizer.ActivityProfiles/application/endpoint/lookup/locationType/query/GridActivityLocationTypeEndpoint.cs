using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.dto.request.filter;
using Sydowwe.Framework.application.dto.response.generic;
using Sydowwe.Framework.application.endpoint.@base.read.pageFilterSort;

namespace AdhdTimeOrganizer.ActivityProfiles.application.endpoint.lookup.locationType.query;

public class GridActivityLocationTypeEndpoint(DbContext dbContext)
    : BaseGridEndpoint<ActivityLocationType, LookupResponse<ActivityLocationType>, LookupFilter>(dbContext)
{
    protected override IQueryable<ActivityLocationType> ApplyCustomFiltering(IQueryable<ActivityLocationType> query, LookupFilter filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.Text))
            query = query.Where(x => x.Text.Contains(filter.Text));
        return query;
    }
}