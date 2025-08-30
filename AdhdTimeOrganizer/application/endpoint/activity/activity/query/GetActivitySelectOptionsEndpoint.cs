using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper.activity;
using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activity.activity.query;

public class GetActivitySelectOptionsEndpoint(
    AppCommandDbContext appDbContext,
    ActivityMapper mapper)
    : BaseGetSelectOptionsEndpoint<Activity, ActivityMapper>(appDbContext, mapper)
{
}
