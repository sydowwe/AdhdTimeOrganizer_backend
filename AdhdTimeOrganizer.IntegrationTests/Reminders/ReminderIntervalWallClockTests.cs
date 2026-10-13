using FluentAssertions;
using Moq;
using Sydowwe.Framework.Contracts.reminders;
using Sydowwe.Framework.Contracts.scheduling;
using Sydowwe.Reminders.domain.entity;
using Sydowwe.Reminders.domain.service;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Reminders;

/// <summary>
/// An interval reminder that names a zone keeps that zone's wall clock across a daylight-saving change.
/// <para>
/// Before <c>ReminderDefinition.TimeZoneId</c> the calculator added days to the anchor's fixed UTC offset, so a "daily
/// at 08:00" registered in summer rang at 07:00 all winter — no error, no log line, just an hour off for half the year.
/// Pure schedule math, so no database: every case is a definition and an instant. Bratislava's switches are
/// 2026-10-25 (03:00 CEST → 02:00 CET) and 2027-03-28 (02:00 CET → 03:00 CEST).
/// </para>
/// </summary>
public class ReminderIntervalWallClockTests
{
    private const string Bratislava = "Europe/Bratislava";

    private static readonly ICronEvaluator NoCron = Mock.Of<ICronEvaluator>();

    [Fact(DisplayName = "A daily 08:00 registered in summer still rings at 08:00 after the switch to winter time")]
    public void Daily_KeepsLocalTimeAcrossFallBack()
    {
        // 08:00 CEST.
        var anchor = Utc(2026, 10, 20, 6, 0);
        var from = Utc(2026, 10, 26, 0, 0);

        Next(Interval(ReminderIntervalPreset.Daily, anchor, Bratislava), from)
            .Should().Be(Utc(2026, 10, 26, 7, 0), "08:00 CET is 07:00 UTC");

        Next(Interval(ReminderIntervalPreset.Daily, anchor, timeZoneId: null), from)
            .Should().Be(Utc(2026, 10, 26, 6, 0), "without a zone the anchor's summer offset is kept — the drift the zone exists to fix");
    }

    [Fact(DisplayName = "A daily 02:30 moves to the end of the spring-forward gap, then returns to 02:30")]
    public void Daily_SpringForwardGap_MovesToEndOfGap()
    {
        // 02:30 CET the day before the switch.
        var anchor = Utc(2027, 3, 27, 1, 30);

        var occurrences = Project(Interval(ReminderIntervalPreset.Daily, anchor, Bratislava), Utc(2027, 3, 27, 12, 0), count: 2);

        occurrences.Should().Equal(
            Utc(2027, 3, 28, 1, 0), // 02:30 never happens that night; the gap closes at 03:00 CEST
            Utc(2027, 3, 29, 0, 30)); // 02:30 CEST
    }

    [Fact(DisplayName = "A daily 02:30 on the fall-back night rings once, at the standard-time 02:30")]
    public void Daily_FallBackOverlap_TakesStandardTime()
    {
        // 02:30 CEST the day before the switch.
        var anchor = Utc(2026, 10, 24, 0, 30);

        var occurrences = Project(Interval(ReminderIntervalPreset.Daily, anchor, Bratislava), Utc(2026, 10, 24, 12, 0), count: 2);

        occurrences.Should().Equal(
            Utc(2026, 10, 25, 1, 30), // 02:30 happens twice; WallClockZone takes the standard-time one
            Utc(2026, 10, 26, 1, 30)); // 02:30 CET
    }

    [Fact(DisplayName = "A monthly 09:00 keeps 09:00 local when the month crosses into summer time")]
    public void Monthly_KeepsLocalTimeAcrossSpringForward()
    {
        // 09:00 CET.
        var anchor = Utc(2026, 3, 15, 8, 0);

        Project(Interval(ReminderIntervalPreset.Monthly, anchor, Bratislava), anchor, count: 2)
            .Should().Equal(anchor, Utc(2026, 4, 15, 7, 0));
    }

    [Fact(DisplayName = "Months from a winter anchor, a summer day's occurrence is that day's 08:00, not the one after")]
    public void Daily_FarFromAnchor_ReturnsThatDaysOccurrence()
    {
        // 08:00 CET; `from` is 30 minutes before 1 July's 08:00 CEST (06:00 UTC). The index search starts from a
        // fixed-offset estimate, and this is the case where the wall-clock occurrence sits an hour earlier than it.
        var anchor = Utc(2026, 1, 10, 7, 0);

        Next(Interval(ReminderIntervalPreset.Daily, anchor, Bratislava), Utc(2026, 7, 1, 5, 30))
            .Should().Be(Utc(2026, 7, 1, 6, 0));
    }

    [Fact(DisplayName = "A stored zone that no longer resolves falls back to the fixed offset instead of stopping the reminder")]
    public void UnknownStoredZone_FallsBackToFixedOffset()
    {
        var anchor = Utc(2026, 10, 20, 6, 0);

        Next(Interval(ReminderIntervalPreset.Daily, anchor, "Not/AZone"), Utc(2026, 10, 26, 0, 0))
            .Should().Be(Utc(2026, 10, 26, 6, 0));
    }

    private static ReminderDefinition Interval(ReminderIntervalPreset preset, DateTimeOffset anchor, string? timeZoneId) => new()
    {
        OwnerModule = "WallClockTest",
        SubjectType = "Subject",
        SubjectId = "1",
        Kind = "Kind",
        TemplateKey = "wall-clock-test",
        ScheduleType = ReminderScheduleType.RecurringInterval,
        IntervalPreset = preset,
        AnchorDate = anchor,
        TimeZoneId = timeZoneId
    };

    private static DateTimeOffset? Next(ReminderDefinition definition, DateTimeOffset from) =>
        ReminderOccurrenceCalculator.ComputeNextOccurrence(definition, from, NoCron);

    private static IEnumerable<DateTimeOffset> Project(ReminderDefinition definition, DateTimeOffset from, int count) =>
        ReminderOccurrenceCalculator.Project(definition, from, NoCron, count).Occurrences.Select(o => o.OccursAt);

    private static DateTimeOffset Utc(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, TimeSpan.Zero);
}
