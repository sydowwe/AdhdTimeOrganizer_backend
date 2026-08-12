using AdhdTimeOrganizer.ActivityProfiles.application.dto.request;
using AdhdTimeOrganizer.ActivityProfiles.application.validator;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.ActivityProfiles.application.endpoint.memoryAnchor.command;

public class UpdateMemoryAnchorEndpoint(DbContext dbContext)
    : BaseUpdateEndpoint<MemoryAnchor, MemoryAnchorRequest>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Validator<UpdateMemoryAnchorValidator>();
    }
}