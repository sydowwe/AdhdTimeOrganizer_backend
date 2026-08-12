using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.ActivityProfiles.application.endpoint.memoryAnchor.command;

public class BatchDeleteMemoryAnchorEndpoint(DbContext dbContext)
    : BaseBatchDeleteEndpoint<MemoryAnchor>(dbContext);