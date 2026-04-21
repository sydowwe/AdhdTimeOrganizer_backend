using Google.Apis.Calendar.v3;

namespace AdhdTimeOrganizer.domain.extServiceContract.googleCalendar;

public interface IGoogleCalendarService
{
    string GetAuthUrl();
    Task<string?> ExchangeCodeForRefreshToken(string code);
    CalendarService GetCalendarService(string refreshToken);
}
