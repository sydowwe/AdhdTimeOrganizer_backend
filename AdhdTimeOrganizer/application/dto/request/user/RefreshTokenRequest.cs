namespace AdhdTimeOrganizer.application.dto.request.user;

public record RefreshTokenRequest
{
    public string? RefreshToken { get; init; } // For extension (body), web uses cookie
}
