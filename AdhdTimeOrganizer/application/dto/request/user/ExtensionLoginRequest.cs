namespace AdhdTimeOrganizer.application.dto.request.user;

public record ExtensionLoginRequest : PasswordLoginRequest
{
    // Inherits Email, Password, RecaptchaToken, Timezone, StayLoggedIn
}
