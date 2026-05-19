using AdhdTimeOrganizer.application.dto.response.activity.memoryAnchor;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper;
using AdhdTimeOrganizer.domain.model.entity.activity.memoryAnchor;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activity.memoryAnchor.query;

public class GetByIdMemoryAnchorEndpoint(AppDbContext dbContext, MemoryAnchorMapper mapper)
    : BaseGetByIdEndpoint<MemoryAnchor, MemoryAnchorResponse, MemoryAnchorMapper>(dbContext, mapper);
