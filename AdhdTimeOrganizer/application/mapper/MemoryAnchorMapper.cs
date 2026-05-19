using AdhdTimeOrganizer.application.dto.request.activity.memoryAnchor;
using AdhdTimeOrganizer.application.dto.response.activity.memoryAnchor;
using AdhdTimeOrganizer.application.mapper.@interface;
using AdhdTimeOrganizer.domain.model.entity.activity.memoryAnchor;
using Riok.Mapperly.Abstractions;

namespace AdhdTimeOrganizer.application.mapper;

[Mapper]
public partial class MemoryAnchorMapper : IBaseSimpleCrudMapper<MemoryAnchor, MemoryAnchorRequest, MemoryAnchorResponse>
{
    public partial MemoryAnchorResponse ToResponse(MemoryAnchor entity);
    public partial void UpdateEntity(MemoryAnchorRequest request, MemoryAnchor entity);
    public partial MemoryAnchor ToEntity(MemoryAnchorRequest request, long userId);
    public partial IQueryable<MemoryAnchorResponse> ProjectToResponse(IQueryable<MemoryAnchor> source);
}
