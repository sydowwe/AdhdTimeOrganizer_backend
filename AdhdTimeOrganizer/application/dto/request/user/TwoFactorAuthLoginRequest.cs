namespace AdhdTimeOrganizer.application.dto.request.user;

public record TwoFactorAuthLoginRequest : TwoFactorAuthRequest
{
    public bool StayLoggedIn { get; set; }
    // public bool RememberClient { get; set; }
}