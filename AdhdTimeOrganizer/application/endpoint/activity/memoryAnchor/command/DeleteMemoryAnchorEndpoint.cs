using AdhdTimeOrganizer.domain.model.entity.activity.memoryAnchor;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.application.endpoint.activity.memoryAnchor.command;

public class DeleteMemoryAnchorEndpoint(AppDbContext dbContext)
    : BaseDeleteEndpoint<MemoryAnchor>(dbContext);