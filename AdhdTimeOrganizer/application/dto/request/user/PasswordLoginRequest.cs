namespace AdhdTimeOrganizer.application.dto.request.user;

public record PasswordLoginRequest : LoginRequest
{
    public required string Email { get; set; }
    public required string Password { get; set; }
}