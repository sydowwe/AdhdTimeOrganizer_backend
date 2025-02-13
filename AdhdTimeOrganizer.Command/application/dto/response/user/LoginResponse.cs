using AdhdTimeOrganizer.Common.domain.model.@enum;

namespace AdhdTimeOrganizer.Command.application.dto.response.user;

public class LoginResponse : EmailResponse
{
    public bool RequiresTwoFactor { get; set; }
    public AvailableLocales CurrentLocale { get; set; }
}