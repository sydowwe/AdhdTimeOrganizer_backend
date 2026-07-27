using AdhdTimeOrganizer.domain.model.entity.activity.lookup;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.application.endpoint.activity.lookup.experienceType.command;

public class BatchDeleteActivityExperienceTypeEndpoint(AppDbContext dbContext)
    : BaseBatchDeleteEndpoint<ActivityExperienceType>(dbContext);