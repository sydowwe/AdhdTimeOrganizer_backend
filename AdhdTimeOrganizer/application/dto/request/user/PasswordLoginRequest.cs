namespace AdhdTimeOrganizer.application.dto.request.user;

public record PasswordLoginRequest : LoginRequest
{
    public required string Email { get; init; }
    public required string Password { get; init; }
}