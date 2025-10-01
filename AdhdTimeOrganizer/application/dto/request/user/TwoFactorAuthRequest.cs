using AdhdTimeOrganizer.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.request.user;

public record TwoFactorAuthRequest : IMyRequest
{
    public required string Token { get; set; }
}