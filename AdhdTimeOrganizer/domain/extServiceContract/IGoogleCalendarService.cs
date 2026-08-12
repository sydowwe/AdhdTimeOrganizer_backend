using Google.Apis.Calendar.v3;

namespace AdhdTimeOrganizer.domain.extServiceContract.googleCalendar;

public interface IGoogleCalendarService
{
    string GenerateState();
    string GetAuthUrl(string state);
    Task<string?> ExchangeCodeForRefreshToken(string code);
    CalendarService GetCalendarService(string refreshToken);
}