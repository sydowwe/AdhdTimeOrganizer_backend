using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.Common.domain.model.@enum;

namespace AdhdTimeOrganizer.Command.application.dto.request.user;

public record UserRequest : EmailRequest
{
    public bool TwoFactorEnabled { get; init; } = false;
    [Required] public required string RecaptchaToken { get; init; }
    [Required] public required AvailableLocales CurrentLocale { get; init; }
    [Required] public required string Timezone { get; init; }
}