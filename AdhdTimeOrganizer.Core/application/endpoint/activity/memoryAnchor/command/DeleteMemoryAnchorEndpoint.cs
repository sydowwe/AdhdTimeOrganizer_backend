using AdhdTimeOrganizer.Core.domain.model.entity.activity.memoryAnchor;
using Sydowwe.Framework.application.endpoint.@base.command;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.Core.application.endpoint.activity.memoryAnchor.command;

public class DeleteMemoryAnchorEndpoint(DbContext dbContext)
    : BaseDeleteEndpoint<MemoryAnchor>(dbContext);