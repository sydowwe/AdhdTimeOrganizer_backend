using AdhdTimeOrganizer.Core.domain.model.entity.activity.memoryAnchor;
using Sydowwe.Framework.application.endpoint.@base.command;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.Core.application.endpoint.activity.memoryAnchor.command;

public class BatchDeleteMemoryAnchorEndpoint(DbContext dbContext)
    : BaseBatchDeleteEndpoint<MemoryAnchor>(dbContext);