using AdhdTimeOrganizer.application.dto.request.activity.memoryAnchor;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.domain.model.entity.activity.memoryAnchor;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activity.memoryAnchor.command;

public class CreateMemoryAnchorEndpoint(AppDbContext dbContext)
    : BaseCreateEndpoint<MemoryAnchor, MemoryAnchorRequest>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Validator<CreateMemoryAnchorValidator>();
    }
}
