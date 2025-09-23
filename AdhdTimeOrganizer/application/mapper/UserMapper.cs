using AdhdTimeOrganizer.application.dto.request.user;
using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.application.dto.response.user;
using AdhdTimeOrganizer.application.mapper.@interface;
using AdhdTimeOrganizer.domain.model.entity.user;
using Riok.Mapperly.Abstractions;

namespace AdhdTimeOrganizer.application.mapper;

// Example DTOs

[Mapper]
public partial class UserMapper : IBaseCreateWithoutUserMapper<User, CreateUserRequest>, IBaseUpdateMapper<User, UserRequest>, IBaseReadMapper<User, UserResponse>
{
    public partial UserResponse ToResponse(User entity);


    public partial User ToEntity(CreateUserRequest request);

    public partial void UpdateEntity(UserRequest request, User entity);

    public SelectOptionResponse ToSelectOptionResponse(User entity)
        => new(entity.Id, entity.UserName); // Assuming User has Name property

    public partial IQueryable<UserResponse> ProjectToResponse(IQueryable<User> query);
}