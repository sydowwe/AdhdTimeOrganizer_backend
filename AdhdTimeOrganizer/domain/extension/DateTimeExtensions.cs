namespace AdhdTimeOrganizer.domain.extension;

public static class DateTimeExtensions
{
    public static bool IsWeekend(this DateTime date) => date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    public static DateTime StartOfWorkDay(this DateTime date) => new(date.Year, date.Month, date.Day, 6, 0, 0);
    public static DateTime EndOfWorkDay(this DateTime date) => new(date.Year, date.Month, date.Day, 14, 0, 0);
}