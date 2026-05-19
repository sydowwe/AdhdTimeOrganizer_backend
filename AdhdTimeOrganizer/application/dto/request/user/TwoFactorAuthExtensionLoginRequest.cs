namespace AdhdTimeOrganizer.application.dto.request.user;

public record TwoFactorAuthExtensionLoginRequest : TwoFactorAuthRequest
{
    public required string Email { get; set; }
    public bool StayLoggedIn { get; set; }
    public required string PendingAuthToken { get; set; }
}
