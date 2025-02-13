using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.Common.domain.model.@enum;

namespace AdhdTimeOrganizer.Command.application.dto.request.user;

public record RegistrationRequest(
    string Email,
    bool TwoFactorEnabled,
    string RecaptchaToken,
    AvailableLocales CurrentLocale,
    string Timezone,
    bool IsOAuth2Only,

    [Required] string Password
) : UserRequest(Email.ToLower(), TwoFactorEnabled, RecaptchaToken, CurrentLocale, Timezone, IsOAuth2Only);