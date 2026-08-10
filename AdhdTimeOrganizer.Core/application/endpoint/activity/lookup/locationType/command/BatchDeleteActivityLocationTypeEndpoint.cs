using AdhdTimeOrganizer.Core.domain.model.entity.activity.lookup;
using Sydowwe.Framework.application.endpoint.@base.command;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.Core.application.endpoint.activity.lookup.locationType.command;

public class BatchDeleteActivityLocationTypeEndpoint(DbContext dbContext)
    : BaseBatchDeleteEndpoint<ActivityLocationType>(dbContext);