using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.application.mapper.@interface;
using AdhdTimeOrganizer.domain.model.entity.user;
using Riok.Mapperly.Abstractions;

namespace AdhdTimeOrganizer.application.mapper;

// Example DTOs

[Mapper]
public partial class UserRoleMapper : IBaseSelectOptionMapper<UserRole>
{
    public SelectOptionResponse ToSelectOptionResponse(UserRole entity)
        => new(entity.Id, entity.Name); // Assuming UserRole has Name property
}