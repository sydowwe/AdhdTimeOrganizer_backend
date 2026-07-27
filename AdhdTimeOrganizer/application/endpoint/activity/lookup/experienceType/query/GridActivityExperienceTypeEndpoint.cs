using AdhdTimeOrganizer.domain.model.entity.activity.lookup;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.dto.request.filter;
using Sydowwe.Framework.application.dto.response.generic;
using Sydowwe.Framework.application.endpoint.@base.read.pageFilterSort;

namespace AdhdTimeOrganizer.application.endpoint.activity.lookup.experienceType.query;

public class GridActivityExperienceTypeEndpoint(AppDbContext dbContext)
    : BaseGridEndpoint<ActivityExperienceType, LookupResponse<ActivityExperienceType>, LookupFilter>(dbContext)
{
    protected override IQueryable<ActivityExperienceType> ApplyCustomFiltering(IQueryable<ActivityExperienceType> query, LookupFilter filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.Text))
            query = query.Where(x => x.Text.Contains(filter.Text));
        return query;
    }
}