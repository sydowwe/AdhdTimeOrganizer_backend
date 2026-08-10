using AdhdTimeOrganizer.Core.domain.model.entity.activity.lookup;
using Sydowwe.Framework.application.endpoint.@base.command;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.Core.application.endpoint.activity.lookup.experienceType.command;

public class BatchDeleteActivityExperienceTypeEndpoint(DbContext dbContext)
    : BaseBatchDeleteEndpoint<ActivityExperienceType>(dbContext);