using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.Planning.domain.service;
using FluentAssertions;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Services;

/// <summary>
/// P3-01's night without a database: which night contains a moment, and when it ends. The calendar read that feeds
/// it, and the deferral it drives, are pinned against real rows in <c>Endpoints.QuietHoursFollowSleepScheduleTests</c>.
/// </summary>
public class SleepNightTests
{
    private static readonly DateOnly Monday = new(2026, 11, 9);
    private static readonly TimeZoneInfo Bratislava = TimeZoneInfo.FindSystemTimeZoneById("Europe/Bratislava");

    private static UserSleepWindow W(int wakeHour, int wakeMinute, int bedHour, int bedMinute) =>
        new(new TimeOnly(wakeHour, wakeMinute), new TimeOnly(bedHour, bedMinute));

    private static DateTimeOffset Utc(DateOnly date, int hour, int minute = 0) =>
        new(date.ToDateTime(new TimeOnly(hour, minute)), TimeSpan.Zero);

    [Fact(DisplayName = "P3-01: the night runs from the day's bed time to the next day's own wake-up")]
    public void TheNight_EndsAtTheNextDaysWakeUp_NotTheDefault()
    {
        var days = new Dictionary<DateOnly, UserSleepWindow>
        {
            [Monday] = W(7, 0, 23, 0),
            [Monday.AddDays(1)] = W(5, 30, 23, 0)
        };

        SleepNight.WakeAt(Utc(Monday, 23, 30), TimeZoneInfo.Utc, d => days.GetValueOrDefault(d, W(7, 0, 23, 0)))
            .Should().Be(Utc(Monday.AddDays(1), 5, 30), "Tuesday's early alarm is what ends Monday night");
    }

    [Fact(DisplayName = "P3-01: before bed and after the alarm the user is awake")]
    public void OutsideTheNight_IsAwake()
    {
        var day = W(7, 0, 23, 0);

        SleepNight.WakeAt(Utc(Monday, 22, 59), TimeZoneInfo.Utc, _ => day).Should().BeNull();
        SleepNight.WakeAt(Utc(Monday, 7, 0), TimeZoneInfo.Utc, _ => day).Should().BeNull("the window is half-open: the alarm itself is awake");
        SleepNight.WakeAt(Utc(Monday, 6, 59), TimeZoneInfo.Utc, _ => day).Should().Be(Utc(Monday, 7, 0),
            "early Monday morning belongs to Sunday's night");
    }

    [Fact(DisplayName = "P3-01: a bed time after midnight starts the night on the next date")]
    public void ABedTimeAfterMidnight_StartsOnTheNextDate()
    {
        var day = W(8, 0, 1, 30);

        SleepNight.WakeAt(Utc(Monday, 23, 59), TimeZoneInfo.Utc, _ => day).Should().BeNull("01:30 is tonight's bed time, not this evening's");
        SleepNight.WakeAt(Utc(Monday.AddDays(1), 2, 0), TimeZoneInfo.Utc, _ => day).Should().Be(Utc(Monday.AddDays(1), 8, 0));
    }

    [Fact(DisplayName = "P3-01: the 00:00 / 08:00 fallback is a night from midnight to eight")]
    public void TheFallback_IsMidnightToEight()
    {
        SleepNight.WakeAt(Utc(Monday, 3, 0), TimeZoneInfo.Utc, _ => UserSleepWindow.Fallback).Should().Be(Utc(Monday, 8, 0));
        SleepNight.WakeAt(Utc(Monday, 23, 0), TimeZoneInfo.Utc, _ => UserSleepWindow.Fallback).Should().BeNull();
    }

    [Fact(DisplayName = "P3-01: a night shift is a daytime night")]
    public void ANightShift_IsADaytimeNight()
    {
        // Up at 16:00, to bed at 08:00 the next morning: the quiet stretch is the morning after.
        var day = W(16, 0, 8, 0);

        SleepNight.WakeAt(Utc(Monday, 10, 0), TimeZoneInfo.Utc, _ => day).Should().Be(Utc(Monday, 16, 0));
        SleepNight.WakeAt(Utc(Monday, 3, 0), TimeZoneInfo.Utc, _ => day).Should().BeNull("03:00 is the middle of the shift");
    }

    [Fact(DisplayName = "P3-01: bed time equal to wake-up time is no night at all")]
    public void BedEqualToWake_IsNoNight()
    {
        var day = W(8, 0, 8, 0);

        foreach (var hour in new[] { 0, 3, 7, 8, 12, 23 })
            SleepNight.WakeAt(Utc(Monday, hour), TimeZoneInfo.Utc, _ => day).Should().BeNull($"nothing is held at {hour:00}:00");
    }

    [Fact(DisplayName = "P3-01: the night is read on the user's own clock, across the autumn DST change")]
    public void TheNight_IsReadInTheUsersZone_AcrossDst()
    {
        // Saturday 24 Oct 2026 → Sunday 25 Oct, when Bratislava falls back from UTC+2 to UTC+1 at 03:00.
        var saturday = new DateOnly(2026, 10, 24);
        var day = W(7, 0, 23, 0);

        // 23:30 Saturday local is 21:30Z; 07:00 Sunday local is 06:00Z after the change.
        SleepNight.WakeAt(Utc(saturday, 21, 30), Bratislava, _ => day).Should().Be(Utc(saturday.AddDays(1), 6, 0));
        SleepNight.WakeAt(Utc(saturday, 20, 30), Bratislava, _ => day).Should().BeNull("22:30 local is before bed");
    }
}
