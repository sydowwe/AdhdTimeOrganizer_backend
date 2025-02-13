using System.ComponentModel.DataAnnotations;

namespace AdhdTimeOrganizer.Command.application.dto.request.user;

public record GoogleSignInRequest(
    string RecaptchaToken,
    string Timezone,
    [ Required] string Code,
    bool StayLoggedIn = false
) : LoginRequest(RecaptchaToken, Timezone, StayLoggedIn);