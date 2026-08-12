using Sydowwe.Framework.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.request.user;

public record GoogleSignInRequest : ICreateRequest
{
    public required string Timezone { get; init; }
    public required string Code { get; init; }
}