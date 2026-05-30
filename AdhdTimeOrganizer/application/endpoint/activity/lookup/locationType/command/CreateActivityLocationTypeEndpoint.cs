using AdhdTimeOrganizer.application.dto.request.generic;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.domain.model.entity.activity.lookup;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activity.lookup.locationType.command;

public class CreateActivityLocationTypeEndpoint(AppDbContext dbContext)
    : BaseCreateEndpoint<ActivityLocationType, SelectOptionRequest<ActivityLocationType>>(dbContext);
