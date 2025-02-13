using AdhdTimeOrganizer.Common.domain.model.@enum;

namespace AdhdTimeOrganizer.Command.application.@interface.users;

public interface IUserSessionService
{
    string Timezone { get; set; }
    AvailableLocales Locale { get; set; }
    void UserLogin(AvailableLocales locale, string timezone); // Keep as a method
}