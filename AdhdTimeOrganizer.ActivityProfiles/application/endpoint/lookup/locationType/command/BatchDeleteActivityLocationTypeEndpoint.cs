using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.ActivityProfiles.application.endpoint.lookup.locationType.command;

public class BatchDeleteActivityLocationTypeEndpoint(DbContext dbContext)
    : BaseBatchDeleteEndpoint<ActivityLocationType>(dbContext);