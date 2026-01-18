using AdhdTimeOrganizer.application.dto.filter;
using AdhdTimeOrganizer.application.dto.response.user;
using AdhdTimeOrganizer.application.endpoint.@base.read.withoutUser;
using AdhdTimeOrganizer.application.helper;
using AdhdTimeOrganizer.domain.model.entity.user;
using AdhdTimeOrganizer.infrastructure.persistence;
using UserMapper = AdhdTimeOrganizer.application.mapper.UserMapper;

namespace AdhdTimeOrganizer.application.endpoint.user.read;

public class UserGetTableEndpoint(AppCommandDbContext dbContext, UserMapper mapper)
    : BaseWithoutUserFilteredPaginatedEndpoint<User, UserResponse, UserFilter, UserMapper>(dbContext, mapper)
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
}