using AdhdTimeOrganizer.application.dto.request.activity.memoryAnchor;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.domain.model.entity.activity.memoryAnchor;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.application.endpoint.activity.memoryAnchor.command;

public class UpdateMemoryAnchorEndpoint(AppDbContext dbContext)
    : BaseUpdateEndpoint<MemoryAnchor, MemoryAnchorRequest>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Validator<UpdateMemoryAnchorValidator>();
    }
}