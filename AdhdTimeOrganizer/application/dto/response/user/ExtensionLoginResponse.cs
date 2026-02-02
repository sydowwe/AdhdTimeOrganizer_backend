namespace AdhdTimeOrganizer.application.dto.response.user;

public record ExtensionLoginResponse : LoginResponse
{
    public string? AccessToken { get; init; }
    public string? RefreshToken { get; init; }
}
