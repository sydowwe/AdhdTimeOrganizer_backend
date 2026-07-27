using Sydowwe.Framework.application.dto.response.@base;
using Sydowwe.Framework.domain.entity.user;

namespace Sydowwe.Framework.application.dto.response.user;

public record UserResponse : IdResponse, IProjectionResponse<UserResponse, BaseUser>
{
    public required string Email { get; init; }
    public bool TwoFactorEnabled { get; init; }

    public static IQueryable<UserResponse> Projection(IQueryable<BaseUser> query) =>
        query.Select(u => new UserResponse
        {
            Id = u.Id,
            Email = u.Email!,
            TwoFactorEnabled = u.TwoFactorEnabled
        });
}