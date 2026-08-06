using AdhdTimeOrganizer.domain.model.entity.activity.lookup;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.application.endpoint.activity.lookup.locationType.command;

public class BatchDeleteActivityLocationTypeEndpoint(AppDbContext dbContext)
    : BaseBatchDeleteEndpoint<ActivityLocationType>(dbContext);