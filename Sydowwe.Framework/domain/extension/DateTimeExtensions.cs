namespace Sydowwe.Framework.domain.extension;

public static class DateTimeExtensions
{
    extension(DateTime date)
    {
        public bool IsWeekend() => date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

        public DateTime StartOfWorkDay() => new(date.Year, date.Month, date.Day, 6, 0, 0);

        public DateTime EndOfWorkDay() => new(date.Year, date.Month, date.Day, 14, 0, 0);
    }
}