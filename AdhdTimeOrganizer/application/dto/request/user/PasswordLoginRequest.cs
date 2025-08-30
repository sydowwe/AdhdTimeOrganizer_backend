namespace AdhdTimeOrganizer.application.dto.request.user;

public record PasswordLoginRequest : LoginRequest
{
    public string Email { get; set; }
    public string Password { get; set; }
}