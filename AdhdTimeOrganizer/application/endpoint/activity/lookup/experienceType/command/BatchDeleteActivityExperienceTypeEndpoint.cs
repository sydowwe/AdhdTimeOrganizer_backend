using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.domain.model.entity.activity.lookup;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activity.lookup.experienceType.command;

public class BatchDeleteActivityExperienceTypeEndpoint(AppDbContext dbContext)
    : BaseBatchDeleteEndpoint<ActivityExperienceType>(dbContext);
