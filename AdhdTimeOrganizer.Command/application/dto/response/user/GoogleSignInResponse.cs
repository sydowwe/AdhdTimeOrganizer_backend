using AdhdTimeOrganizer.Common.domain.model.@enum;

namespace AdhdTimeOrganizer.Command.application.dto.response.user;

public record GoogleSignInResponse : EmailResponse
{
    public required AvailableLocales CurrentLocale { get; init; }
}