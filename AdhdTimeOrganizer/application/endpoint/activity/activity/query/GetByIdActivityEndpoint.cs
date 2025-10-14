using AdhdTimeOrganizer.application.dto.response.activity;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper.activity;
using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.infrastructure.persistence;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activity.activity.query;

public class GetByIdActivityEndpoint(
    AppCommandDbContext dbContext,
    ActivityMapper mapper)
    : BaseGetByIdEndpoint<Activity, ActivityResponse, ActivityMapper>(dbContext, mapper)
{
    protected override IQueryable<Activity> WithIncludes(IQueryable<Activity> query)
    {
        return query
            .Include(a => a.Role)
            .Include(a => a.Category);
    }
}
