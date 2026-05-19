using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.domain.model.entity.activity.memoryAnchor;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activity.memoryAnchor.command;

public class BatchDeleteMemoryAnchorEndpoint(AppDbContext dbContext)
    : BaseBatchDeleteEndpoint<MemoryAnchor>(dbContext);
