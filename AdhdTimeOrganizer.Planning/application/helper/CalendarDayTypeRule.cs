using AdhdTimeOrganizer.Core.domain.model.@enum;
using AdhdTimeOrganizer.Planning.domain.model.entity;

namespace AdhdTimeOrganizer.Planning.application.helper;

/// <summary>
/// The one rule tying a day's <see cref="DayType"/> to the weekday it actually falls on. Shared by
/// <c>UpdateCalendarEndpoint</c> (which turns a violation into a 400) and
/// <c>BatchUpdateDayTypeCalendarEndpoint</c> (which turns it into that one day's <c>succeeded: false</c>) —
/// two different failure shapes over one rule, so the rule returns a message rather than throwing.
/// <para>
/// It cannot live in a FluentValidation validator: neither caller has the date in its request. Both read it
/// off the loaded <see cref="Calendar"/>, whose <c>Date</c> is <c>init</c>-only and never changes.
/// </para>
/// </summary>
public static class CalendarDayTypeRule
{
    /// <summary>The reason <paramref name="dayType"/> is impossible on this day, or <c>null</c> when it is fine.</summary>
    public static string? Violation(Calendar calendar, DayType dayType) => dayType switch
    {
        DayType.Weekend when !calendar.IsWeekend =>
            $"DayType 'Weekend' is only valid on Saturday or Sunday, but date {calendar.Date} is a {calendar.Date.DayOfWeek}",
        DayType.Workday when calendar.IsWeekend =>
            $"DayType 'Workday' is not valid on weekends, but date {calendar.Date} is a {calendar.Date.DayOfWeek}",
        _ => null
    };
}
