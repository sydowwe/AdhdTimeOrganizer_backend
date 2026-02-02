namespace AdhdTimeOrganizer.application.dto.response.user;

public record RefreshTokenResponse
{
    public required string AccessToken { get; init; }
    public required string RefreshToken { get; init; }
}
