using System.ComponentModel.DataAnnotations;

namespace AdhdTimeOrganizer.Command.application.dto.request.user;

public record PasswordLoginRequest(
    [ Required, EmailAddress] string Email,
    [ Required] string Password,

    string Timezone,
    string RecaptchaToken,
    bool StayLoggedIn = false
) : LoginRequest(RecaptchaToken, Timezone, StayLoggedIn);