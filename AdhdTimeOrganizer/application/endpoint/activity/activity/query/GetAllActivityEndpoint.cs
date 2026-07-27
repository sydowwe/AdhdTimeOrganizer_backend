using AdhdTimeOrganizer.application.dto.response.activity;
using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.application.endpoint.activity.activity.query;

public class GetAllActivityEndpoint(
    AppDbContext dbContext)
    : BaseGetAllEndpoint<Activity, ActivityResponse>(dbContext)
{
}