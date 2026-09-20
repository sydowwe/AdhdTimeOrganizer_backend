using AdhdTimeOrganizer.Planning.domain.service;
using FluentAssertions;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Services;

/// <summary>
/// The Slovak holiday table and the Computus behind it. Pure, static and dependency-free — no database.
/// <para>
/// It feeds <c>CalendarDayFactory</c>, which feeds <b>both</b> creators of a calendar row
/// (<c>CalendarSeeder</c> for whole years, <c>CalendarProvisioner</c> on a day's first task), so a wrong
/// date here is written into every user's calendar and is only visible if someone looks at that day.
/// </para>
/// <para>
/// The end of the chain — that a lazily created holiday keeps its key and name and stays a <c>Workday</c> —
/// is pinned in <c>Endpoints.LazyCalendarCreationTests</c>.
/// </para>
/// </summary>
public class HolidayCalendarTests
{
    // ─── Part 1: the Computus ────────────────────────────────────────────────

    /// <summary>
    /// Known Gregorian Easter Sundays, driven through the public surface because <c>CalculateEaster</c> is
    /// private: Good Friday is Easter − 2 and Easter Monday is Easter + 1, so matching both pins the
    /// arithmetic and the two derived entries at once.
    /// <para>
    /// 2038 (25 April) is the latest date Easter can fall on and 2035 (25 March) is near the earliest
    /// (22 March) — a Computus that is right for the common years and wrong at the boundaries is the usual
    /// failure mode.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(2024, 3, 31)]
    [InlineData(2025, 4, 20)]
    [InlineData(2026, 4, 5)]
    [InlineData(2027, 3, 28)]
    [InlineData(2028, 4, 16)]
    [InlineData(2029, 4, 1)]
    [InlineData(2030, 4, 21)]
    [InlineData(2038, 4, 25)]
    [InlineData(2035, 3, 25)]
    public void Easter_LandsOnTheKnownSunday(int year, int month, int day)
    {
        var easter = new DateOnly(year, month, day);

        easter.DayOfWeek.Should().Be(DayOfWeek.Sunday, "every date in this table is an Easter Sunday");

        HolidayCalendar.ForDate(easter.AddDays(-2)).Should().Be(new Holiday("GOOD_FRIDAY", "Veľký piatok"));
        HolidayCalendar.ForDate(easter.AddDays(1)).Should().Be(new Holiday("EASTER_MONDAY", "Veľkonočný pondelok"));
    }

    // ─── Part 2: the fixed table ─────────────────────────────────────────────

    /// <summary>
    /// Every fixed entry, over three years including the leap year 2028, with the key it must carry. The
    /// keys are what the client's locale table is written against, so a rename here breaks a translation
    /// silently — the exact strings are the contract, not just "some key".
    /// </summary>
    [Theory]
    [InlineData(1, 1, "NEW_YEAR")]
    [InlineData(1, 6, "EPIPHANY")]
    [InlineData(5, 1, "LABOUR_DAY")]
    [InlineData(5, 8, "VICTORY_DAY")]
    [InlineData(7, 5, "CYRIL_AND_METHODIUS")]
    [InlineData(8, 29, "SNP_ANNIVERSARY")]
    [InlineData(9, 1, "CONSTITUTION_DAY")]
    [InlineData(9, 15, "OUR_LADY_OF_SORROWS")]
    [InlineData(11, 1, "ALL_SAINTS")]
    [InlineData(11, 17, "FREEDOM_AND_DEMOCRACY_DAY")]
    [InlineData(12, 24, "CHRISTMAS_EVE")]
    [InlineData(12, 25, "CHRISTMAS_DAY")]
    [InlineData(12, 26, "ST_STEPHENS_DAY")]
    public void FixedHoliday_CarriesItsKeyInEveryYear(int month, int day, string expectedKey)
    {
        foreach (var year in new[] { 2027, 2028, 2031 })
        {
            var holiday = HolidayCalendar.ForDate(new DateOnly(year, month, day));

            holiday.Should().NotBeNull($"{year:0000}-{month:00}-{day:00} is a fixed Slovak holiday");
            holiday!.Key.Should().Be(expectedKey);
            holiday.Name.Should().NotBeNullOrEmpty();
        }
    }

    /// <summary>
    /// Days that are holidays next door but not here — 6 July (Jan Hus), 28 September and 28 October are
    /// Czech. The Czech table is gone (B19); these pin that none of its entries leaked into the Slovak one.
    /// </summary>
    [Theory]
    [InlineData(7, 6)]
    [InlineData(9, 28)]
    [InlineData(10, 28)]
    public void CzechHolidays_AreNotSlovakOnes(int month, int day)
    {
        HolidayCalendar.ForDate(new DateOnly(2031, month, day)).Should().BeNull();
    }

    /// <summary>Two exact names, so the native strings themselves are pinned somewhere.</summary>
    [Fact]
    public void NamesTwoEntriesExactly()
    {
        HolidayCalendar.ForDate(new DateOnly(2031, 12, 25))!.Name.Should().Be("Prvý sviatok vianočný");
        HolidayCalendar.ForDate(new DateOnly(2031, 9, 1))!.Name.Should().Be("Deň Ústavy Slovenskej republiky");
    }

    /// <summary>Fifteen entries — thirteen fixed, plus the two Easter ones.</summary>
    [Fact]
    public void EachYear_HasFifteenEntries()
    {
        HolidayCalendar.ForYear(2031).Should().HaveCount(15);
    }

    /// <summary>
    /// A key identifies a holiday, so two dates in one year must never share one — a client's locale table
    /// would render both as the same day. Names are unique for the same reason, one step weaker.
    /// </summary>
    [Fact]
    public void KeysAndNames_AreUniqueWithinAYear()
    {
        var year = HolidayCalendar.ForYear(2031).Values;

        year.Select(h => h.Key).Should().OnlyHaveUniqueItems();
        year.Select(h => h.Name).Should().OnlyHaveUniqueItems();
    }

    // ─── Part 3: the contract ────────────────────────────────────────────────

    [Fact]
    public void AnOrdinaryDay_IsNotAHoliday()
    {
        HolidayCalendar.ForDate(new DateOnly(2029, 3, 3)).Should().BeNull();
    }

    /// <summary>
    /// <c>ForYear</c> builds a dictionary literal and then <c>Add</c>s the two Easter dates. If Easter Monday
    /// or Good Friday ever collided with a fixed entry, <c>Add</c> would throw <c>ArgumentException</c> and
    /// <b>every calendar creation for that year</b> — including user registration — would fail. It should
    /// never fire; one line says so for forty years.
    /// </summary>
    [Fact]
    public void NoYear_BuildsADuplicateKey()
    {
        for (var year = 2020; year <= 2060; year++)
            FluentActions.Invoking(() => HolidayCalendar.ForYear(year)).Should().NotThrow();
    }
}
