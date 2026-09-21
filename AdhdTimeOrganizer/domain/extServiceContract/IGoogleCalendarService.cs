using Google.Apis.Calendar.v3;

namespace AdhdTimeOrganizer.domain.extServiceContract.googleCalendar;

public interface IGoogleCalendarService
{
    /// <summary>
    /// Whether this deployment holds a Google OAuth client id and secret. When <c>false</c> nothing
    /// below can succeed — the status endpoint reports it so the client hides <i>Connect</i>, and the
    /// auth-url endpoint refuses rather than hand out a URL Google will reject.
    /// </summary>
    bool IsConfigured { get; }

    string GenerateState();
    string GetAuthUrl(string state);
    Task<string?> ExchangeCodeForRefreshToken(string code);
    CalendarService GetCalendarService(string refreshToken);
}
