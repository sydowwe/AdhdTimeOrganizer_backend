using AdhdTimeOrganizer.domain.model.@enum;

namespace AdhdTimeOrganizer.application.dto.response.user;

public record GoogleSignInResponse : EmailResponse
{
    public AvailableLocales CurrentLocale { get; init; }
}