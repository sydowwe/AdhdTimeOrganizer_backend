using AdhdTimeOrganizer.application.dto.request.filter;
using AdhdTimeOrganizer.application.dto.response.user;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.domain.helper;
using AdhdTimeOrganizer.domain.model.entity.user;
using AdhdTimeOrganizer.infrastructure.persistence;
using UserMapper = AdhdTimeOrganizer.application.mapper.UserMapper;

namespace AdhdTimeOrganizer.application.endpoint.user.read;

public class UserGetTableEndpoint(AppCommandDbContext dbContext, UserMapper mapper)
    : BaseFilteredPaginatedEndpoint<User, UserResponse, UserFilter>(dbContext)
{
    public override string[] AllowedRoles()
    {
        return EndpointHelper.GetAdminOrHigherRoles();
    }

    protected override IQueryable<User> ApplyCustomFiltering(IQueryable<User> query, UserFilter filter)
    {
        if (filter.Name != null)
        {
            query = query.Where(x => x.UserName != null && x.UserName.Contains(filter.Name));
        }

        if (filter.PhoneNumber != null)
        {
            query = query.Where(x => x.PhoneNumber != null && x.PhoneNumber.Contains(filter.PhoneNumber));
        }

        return query;
    }

    protected override UserResponse MapToResponse(User entity)
    {
        return mapper.ToResponse(entity);
    }
}