using AdhdTimeOrganizer.Core.domain.model.@enum;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using AdhdTimeOrganizer.Planning.domain.service;
using FluentAssertions;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Services;

/// <summary>
/// F4 question 4 (owner decision, 2026-10-15): a public holiday on a weekday is matched like a weekend by the planner,
/// and is <b>not</b> time away. Pure, no database. The days are built by <see cref="CalendarDayFactory"/> from the
/// real <see cref="HolidayCalendar"/>, never by setting <c>HolidayKey</c> by hand, so a change to either fails here.
/// <para>
/// <see cref="RepeatingPlannerTaskSchedule.FallsOn"/> is the one day match behind the suggestion panel and the FO6
/// materialiser, so the cases on it hold for auto-placing too. The materialiser cannot be driven onto a holiday from a
/// test, since its today is the real clock's. The endpoint ends are in
/// <c>Endpoints.TaskPlannerDayTemplateSuggestionsTests</c> and <c>Endpoints.RepeatingPlannerTaskSuggestionsTests</c>,
/// and the streak end is in <c>Routines.RoutineAwayExcusalFlowTests</c>.
/// </para>
/// </summary>
public class HolidayPlanningDayTypesTests
{
    private static readonly DateOnly EasterMonday2028 = new(2028, 4, 17);
    private static readonly DateOnly NewYear2028 = new(2028, 1, 1); // a Saturday

    [Fact(DisplayName = "F4 Q4: a holiday weekday is stored as a Workday and matched as Weekend first, then Workday")]
    public void HolidayWeekday_IsMatchedAsWeekendThenWorkday()
    {
        var day = Day(EasterMonday2028);

        day.DayType.Should().Be(DayType.Workday, "the stored type does not change for a holiday");
        day.HolidayKey.Should().Be("EASTER_MONDAY");
        day.DayType.PlanningDayTypes(day.HolidayKey).Should().Equal(DayType.Weekend, DayType.Workday);
    }

    [Fact(DisplayName = "F4 Q4: an ordinary weekday, a holiday Saturday and a set day type are matched by their stored type alone")]
    public void EveryOtherDay_IsMatchedByItsStoredTypeAlone()
    {
        var plainMonday = Day(EasterMonday2028.AddDays(7));
        plainMonday.HolidayKey.Should().BeNull();
        plainMonday.DayType.PlanningDayTypes(plainMonday.HolidayKey).Should().Equal(DayType.Workday);

        var holidaySaturday = Day(NewYear2028);
        holidaySaturday.HolidayKey.Should().Be("NEW_YEAR");
        holidaySaturday.DayType.PlanningDayTypes(holidaySaturday.HolidayKey).Should().Equal(DayType.Weekend);

        // The user's own type wins over the holiday: a vacation on Easter Monday is a vacation day.
        foreach (var setType in new[] { DayType.Vacation, DayType.SickDay, DayType.Special })
            setType.PlanningDayTypes("EASTER_MONDAY").Should().Equal(setType);
    }

    /// <summary>
    /// The half the owner ruled out. If a holiday ever became an away day, Routines would excuse its cycles, the nudges
    /// would fall silent and the suggestion endpoints would drop both pattern tiers. All of that reads
    /// <c>IsAway</c> on the stored type.
    /// </summary>
    [Fact(DisplayName = "F4 Q4: a holiday is never an away day")]
    public void Holiday_IsNotAway()
    {
        var day = Day(EasterMonday2028);

        day.DayType.IsAway().Should().BeFalse();
        DayTypeExtensions.AwayTypes.Should().NotIntersectWith(day.DayType.PlanningDayTypes(day.HolidayKey));
    }

    [Fact(DisplayName = "F4 Q4: on a holiday weekday a Weekend rule falls as well as a Workday rule, and an away rule does not")]
    public void FallsOn_HolidayWeekday_MatchesWeekendAndWorkdayRules()
    {
        var holiday = Day(EasterMonday2028);
        var plainMonday = Day(EasterMonday2028.AddDays(7));
        var holidaySaturday = Day(NewYear2028);

        var workdayRule = DayTypeRule(DayType.Workday);
        var weekendRule = DayTypeRule(DayType.Weekend);
        var vacationRule = DayTypeRule(DayType.Vacation);

        RepeatingPlannerTaskSchedule.FallsOn(workdayRule, holiday).Should().BeTrue("a workday rule still fires on a holiday");
        RepeatingPlannerTaskSchedule.FallsOn(weekendRule, holiday).Should().BeTrue("most people do not work on a holiday");
        RepeatingPlannerTaskSchedule.FallsOn(vacationRule, holiday).Should().BeFalse("a holiday is not time away");

        RepeatingPlannerTaskSchedule.FallsOn(workdayRule, plainMonday).Should().BeTrue();
        RepeatingPlannerTaskSchedule.FallsOn(weekendRule, plainMonday).Should().BeFalse("the control: only the holiday widens the match");

        RepeatingPlannerTaskSchedule.FallsOn(weekendRule, holidaySaturday).Should().BeTrue();
        RepeatingPlannerTaskSchedule.FallsOn(workdayRule, holidaySaturday).Should().BeFalse("a holiday Saturday is a plain weekend");
    }

    private static Calendar Day(DateOnly date) => CalendarDayFactory.Create(1, date, UserSleepWindow.Fallback);

    private static RepeatingPlannerTask DayTypeRule(DayType dayType) =>
        new()
        {
            UserId = 1,
            ActivityId = 1,
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(10, 0),
            IsBackground = false,
            IsActive = true,
            RecurrenceType = RecurrenceType.DayType,
            ScheduledForDayTypes = [dayType.ToString()]
        };
}
