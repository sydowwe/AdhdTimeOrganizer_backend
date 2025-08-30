using AdhdTimeOrganizer.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.request.user;

public record TwoFactorAuthRequest : IMyRequest
{
    public string Token { get; set; }
}