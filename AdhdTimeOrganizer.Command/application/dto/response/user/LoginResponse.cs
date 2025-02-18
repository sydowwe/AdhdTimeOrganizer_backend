using AdhdTimeOrganizer.Common.domain.model.@enum;

namespace AdhdTimeOrganizer.Command.application.dto.response.user;

public record LoginResponse : EmailResponse
{
    public required bool RequiresTwoFactor { get; init; }
    public required AvailableLocales CurrentLocale { get; init; }
}