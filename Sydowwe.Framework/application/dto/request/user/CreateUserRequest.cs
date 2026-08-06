using Sydowwe.Framework.application.dto.request.@interface;

namespace Sydowwe.Framework.application.dto.request.user;

public record CreateUserRequest : ICreateRequest
{
    public required string Email { get; set; }

    public required long RoleId { get; set; }
    public string? Password { get; set; }
    public string? MicrosoftAccountId { get; set; }
}