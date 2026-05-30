using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.domain.model.entity.user;
using AdhdTimeOrganizer.domain.model.@enum;

namespace AdhdTimeOrganizer.application.dto.request.user;

public record UserRequest : EmailRequest
{
    public required bool TwoFactorEnabled { get; init; }
    [Required] public required AvailableLocales CurrentLocale { get; init; }
    [Required] public required string Timezone { get; init; }

    public User ToEntity =>
        new()
        {
            Email = Email,
            TwoFactorEnabled = TwoFactorEnabled,
            CurrentLocale = CurrentLocale,
            Timezone = TimeZoneInfo.FindSystemTimeZoneById(Timezone)
        };
}