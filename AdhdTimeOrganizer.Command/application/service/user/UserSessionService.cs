using AdhdTimeOrganizer.Command.application.@interface.users;
using AdhdTimeOrganizer.Common.domain.model.@enum;
using Microsoft.AspNetCore.Http;

namespace AdhdTimeOrganizer.Command.application.service.user;

public class UserSessionService(IHttpContextAccessor httpContextAccessor) : IUserSessionService
{
    private readonly ISession _session =
        httpContextAccessor.HttpContext?.Session ?? throw new Exception("No Active HttpContext");

    public long CartId
    {
        get =>  long.TryParse(_session.GetString("CartId"), out var parsedCartId)
                ? parsedCartId
                : throw new InvalidOperationException("CartId missing in session");
        set => _session.SetString("CartId", value.ToString());
    }

    public long FavoriteListId
    {
        get => long.TryParse(_session.GetString("FavoriteListId"), out var parsedFavoriteListId)
                ? parsedFavoriteListId
                : throw new InvalidOperationException("FavoriteListId missing in session");
        set => _session.SetString("FavoriteListId", value.ToString());
    }

    public string Timezone
    {
        get => _session.GetString("Timezone") ?? TimeZoneInfo.Utc.Id; // Default to UTC
        set => _session.SetString("Timezone", value);
    }

    public AvailableLocales Locale
    {
        get => Enum.TryParse(_session.GetString("Locale"), out AvailableLocales locale)
                ? locale
                : AvailableLocales.En;
        set => _session.SetString("Locale", value.ToString());
    }

    public void UserLogin(AvailableLocales locale, string timezone)
    {
        Locale = locale;
        Timezone = timezone;
    }
}