namespace Sydowwe.Framework.application.dto.request.user;

public record LoginRequest
{
    public required bool StayLoggedIn { get; set; }
    public required string RecaptchaToken { get; set; }
    public required string Timezone { get; set; }
}