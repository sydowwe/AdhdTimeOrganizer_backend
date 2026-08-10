using AdhdTimeOrganizer.Core.domain.model.entity.activity.lookup;
using Sydowwe.Framework.application.dto.request.generic;
using Sydowwe.Framework.application.endpoint.@base.command;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.Core.application.endpoint.activity.lookup.locationType.command;

public class CreateActivityLocationTypeEndpoint(DbContext dbContext)
    : BaseCreateLookupEndpoint<ActivityLocationType>(dbContext);