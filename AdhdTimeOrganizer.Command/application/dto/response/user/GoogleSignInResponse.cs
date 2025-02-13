using AdhdTimeOrganizer.Common.domain.model.@enum;

namespace AdhdTimeOrganizer.Command.application.dto.response.user;

public class GoogleSignInResponse : EmailResponse
{
    public AvailableLocales CurrentLocale { get; set; }
}