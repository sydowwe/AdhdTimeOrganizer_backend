using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.Common.domain.model.@enum;

namespace AdhdTimeOrganizer.Command.application.dto.request.user;

public record UserRequest(
    string Email,
    [ Required] bool TwoFactorEnabled,
    [ Required] string RecaptchaToken,
    [ Required] AvailableLocales CurrentLocale,
    [ Required] string Timezone,
    [ Required] bool IsOAuth2Only
) : EmailRequest(Email);