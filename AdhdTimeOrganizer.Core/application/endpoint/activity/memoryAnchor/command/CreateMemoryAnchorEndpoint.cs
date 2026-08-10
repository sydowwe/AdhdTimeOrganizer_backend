using AdhdTimeOrganizer.Core.application.dto.request.activity.memoryAnchor;
using AdhdTimeOrganizer.Core.application.validator;
using AdhdTimeOrganizer.Core.domain.model.entity.activity.memoryAnchor;
using Sydowwe.Framework.application.endpoint.@base.command;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.Core.application.endpoint.activity.memoryAnchor.command;

public class CreateMemoryAnchorEndpoint(DbContext dbContext)
    : BaseCreateEndpoint<MemoryAnchor, MemoryAnchorRequest>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Validator<CreateMemoryAnchorValidator>();
    }
}