namespace AdhdTimeOrganizer.application.dto.response.user;

public record LoginResponse : EmailResponse
{
    public required bool RequiresTwoFactor { get; init; }
}