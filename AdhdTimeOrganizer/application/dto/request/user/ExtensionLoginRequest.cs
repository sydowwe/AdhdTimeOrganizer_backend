namespace AdhdTimeOrganizer.application.dto.request.user;

public record ExtensionLoginRequest
{
    public required string Email { get; init; }
    public required string Password { get; init; }
}
