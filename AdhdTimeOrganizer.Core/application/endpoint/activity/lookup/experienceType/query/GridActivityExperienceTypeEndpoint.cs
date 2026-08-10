using AdhdTimeOrganizer.Core.domain.model.entity.activity.lookup;
using Sydowwe.Framework.application.dto.request.filter;
using Sydowwe.Framework.application.dto.response.generic;
using Sydowwe.Framework.application.endpoint.@base.read.pageFilterSort;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.Core.application.endpoint.activity.lookup.experienceType.query;

public class GridActivityExperienceTypeEndpoint(DbContext dbContext)
    : BaseGridEndpoint<ActivityExperienceType, LookupResponse<ActivityExperienceType>, LookupFilter>(dbContext)
{
    protected override IQueryable<ActivityExperienceType> ApplyCustomFiltering(IQueryable<ActivityExperienceType> query, LookupFilter filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.Text))
            query = query.Where(x => x.Text.Contains(filter.Text));
        return query;
    }
}