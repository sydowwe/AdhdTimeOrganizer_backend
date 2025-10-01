namespace AdhdTimeOrganizer.application.dto.request.user;

public record TwoFactorAuthLoginRequest : TwoFactorAuthRequest
{
    public required string Email { get; set; }
    public bool StayLoggedIn { get; set; }
    // public bool RememberClient { get; set; }
}