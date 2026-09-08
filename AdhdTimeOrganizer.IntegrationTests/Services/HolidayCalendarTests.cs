using AdhdTimeOrganizer.Planning.domain.service;
using FluentAssertions;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Services;

/// <summary>
/// The holiday tables and the Computus behind them. Pure, static and dependency-free — no database.
/// <para>
/// It feeds <c>CalendarDayFactory</c>, which feeds <b>both</b> creators of a calendar row
/// (<c>CalendarSeeder</c> for whole years, <c>CalendarProvisioner</c> on a day's first task), so a wrong
/// date here is written into every user's calendar and is only visible if someone looks at that day.
/// </para>
/// <para>
/// The end of the chain — that a lazily created holiday keeps its name and stays a <c>Workday</c> — is pinned
/// in <c>Endpoints.LazyCalendarCreationTests</c>.
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

        HolidayCalendar.ForDate(easter.AddDays(-2), "SK").Should().Be("Veľký piatok");
        HolidayCalendar.ForDate(easter.AddDays(1), "SK").Should().Be("Veľkonočný pondelok");
    }

    /// <summary>The same arithmetic feeds the Czech table, under Czech names.</summary>
    [Theory]
    [InlineData(2026, 4, 5)]
    [InlineData(2038, 4, 25)]
    [InlineData(2035, 3, 25)]
    public void Easter_FeedsTheCzechTableToo(int year, int month, int day)
    {
        var easter = new DateOnly(year, month, day);

        HolidayCalendar.ForDate(easter.AddDays(-2), "CZ").Should().Be("Velký pátek");
        HolidayCalendar.ForDate(easter.AddDays(1), "CZ").Should().Be("Velikonoční pondělí");
    }

    // ─── Part 2: the fixed tables ────────────────────────────────────────────

    /// <summary>
    /// Every fixed Slovak entry, over three years including the leap year 2028. The dates are what the code
    /// can get wrong; the names are asserted exactly in <see cref="Slovak_NamesTwoEntriesExactly"/>.
    /// </summary>
    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 6)]
    [InlineData(5, 1)]
    [InlineData(5, 8)]
    [InlineData(7, 5)]
    [InlineData(8, 29)]
    [InlineData(9, 1)]
    [InlineData(9, 15)]
    [InlineData(11, 1)]
    [InlineData(11, 17)]
    [InlineData(12, 24)]
    [InlineData(12, 25)]
    [InlineData(12, 26)]
    public void Slovak_FixedHoliday_IsNamedInEveryYear(int month, int day)
    {
        foreach (var year in new[] { 2027, 2028, 2031 })
            HolidayCalendar.ForDate(new DateOnly(year, month, day), "SK")
                .Should().NotBeNullOrEmpty($"{year:0000}-{month:00}-{day:00} is a fixed Slovak holiday");
    }

    /// <summary>Every fixed Czech entry, same three years. Note 5 <b>and</b> 6 July, unlike SK.</summary>
    [Theory]
    [InlineData(1, 1)]
    [InlineData(5, 1)]
    [InlineData(5, 8)]
    [InlineData(7, 5)]
    [InlineData(7, 6)]
    [InlineData(9, 28)]
    [InlineData(10, 28)]
    [InlineData(11, 17)]
    [InlineData(12, 24)]
    [InlineData(12, 25)]
    [InlineData(12, 26)]
    public void Czech_FixedHoliday_IsNamedInEveryYear(int month, int day)
    {
        foreach (var year in new[] { 2027, 2028, 2031 })
            HolidayCalendar.ForDate(new DateOnly(year, month, day), "CZ")
                .Should().NotBeNullOrEmpty($"{year:0000}-{month:00}-{day:00} is a fixed Czech holiday");
    }

    /// <summary>
    /// The two tables are not the same table. 6 January and 29 August are Slovak-only; 28 October is
    /// Czech-only — a copy-paste that collapsed the two would show up here and nowhere else.
    /// </summary>
    [Theory]
    [InlineData(1, 6)]
    [InlineData(8, 29)]
    [InlineData(9, 15)]
    [InlineData(11, 1)]
    public void SlovakOnlyHolidays_AreNotCzechOnes(int month, int day)
    {
        HolidayCalendar.ForDate(new DateOnly(2031, month, day), "CZ").Should().BeNull();
    }

    [Theory]
    [InlineData(7, 6)]
    [InlineData(9, 28)]
    [InlineData(10, 28)]
    public void CzechOnlyHolidays_AreNotSlovakOnes(int month, int day)
    {
        HolidayCalendar.ForDate(new DateOnly(2031, month, day), "SK").Should().BeNull();
    }

    /// <summary>Two exact names per country, so the strings themselves are pinned somewhere.</summary>
    [Fact]
    public void Slovak_NamesTwoEntriesExactly()
    {
        HolidayCalendar.ForDate(new DateOnly(2031, 12, 25), "SK").Should().Be("Prvý sviatok vianočný");
        HolidayCalendar.ForDate(new DateOnly(2031, 9, 1), "SK").Should().Be("Deň Ústavy Slovenskej republiky");
    }

    [Fact]
    public void Czech_NamesTwoEntriesExactly()
    {
        HolidayCalendar.ForDate(new DateOnly(2031, 12, 25), "CZ").Should().Be("1. svátek vánoční");
        HolidayCalendar.ForDate(new DateOnly(2031, 10, 28), "CZ")
            .Should().Be("Den vzniku samostatného československého státu");
    }

    /// <summary>Fifteen Slovak entries, eleven Czech ones — thirteen and eleven fixed, plus two Easter each.</summary>
    [Fact]
    public void EachYear_HasTheExpectedNumberOfEntries()
    {
        HolidayCalendar.ForYear(2031, "SK").Should().HaveCount(15);
        HolidayCalendar.ForYear(2031, "CZ").Should().HaveCount(13);
    }

    // ─── Part 3: the contract ────────────────────────────────────────────────

    [Fact]
    public void AnOrdinaryDay_IsNotAHoliday()
    {
        HolidayCalendar.ForDate(new DateOnly(2029, 3, 3), "SK").Should().BeNull();
        HolidayCalendar.ForDate(new DateOnly(2029, 3, 3), "CZ").Should().BeNull();
    }

    /// <summary>The <c>_ =&gt; []</c> arm: an unknown country is a year with no holidays, not null and not a throw.</summary>
    [Theory]
    [InlineData("DE")]
    [InlineData("")]
    [InlineData("nonsense")]
    public void AnUnknownCountry_IsAnEmptyYear(string countryCode)
    {
        HolidayCalendar.ForYear(2031, countryCode).Should().NotBeNull().And.BeEmpty();
        HolidayCalendar.ForDate(new DateOnly(2031, 12, 25), countryCode).Should().BeNull();
    }

    /// <summary>The <c>ToUpperInvariant()</c> at the switch — a lower-cased code from config still resolves.</summary>
    [Fact]
    public void TheCountryCode_IsCaseInsensitive()
    {
        var upper = HolidayCalendar.ForYear(2031, "SK");

        HolidayCalendar.ForYear(2031, "sk").Should().BeEquivalentTo(upper);
        HolidayCalendar.ForYear(2031, "Sk").Should().BeEquivalentTo(upper);
        HolidayCalendar.ForDate(new DateOnly(2031, 12, 25), "sk")
            .Should().Be(HolidayCalendar.ForDate(new DateOnly(2031, 12, 25), "SK"));
    }

    /// <summary>
    /// <c>DefaultCountryCode</c> is what both callers fall back to, and its own doc says "one place, so the
    /// two callers cannot drift" — this is the assertion that keeps that true.
    /// </summary>
    [Fact]
    public void TheDefaultCountry_IsSlovakia_AndTheDefaultedOverloadsAgreeWithIt()
    {
        HolidayCalendar.DefaultCountryCode.Should().Be("SK");

        HolidayCalendar.ForYear(2031).Should().BeEquivalentTo(HolidayCalendar.ForYear(2031, "SK"));

        for (var date = new DateOnly(2031, 1, 1); date.Year == 2031; date = date.AddDays(1))
            HolidayCalendar.ForDate(date).Should().Be(HolidayCalendar.ForDate(date, "SK"));
    }

    /// <summary>
    /// <c>Slovak()</c> builds a dictionary literal and then <c>Add</c>s the two Easter dates. If Easter Monday
    /// or Good Friday ever collided with a fixed entry, <c>Add</c> would throw <c>ArgumentException</c> and
    /// <b>every calendar creation for that year</b> — including user registration — would fail. It should
    /// never fire; four lines say so for forty years.
    /// </summary>
    [Fact]
    public void NoYear_BuildsADuplicateKey()
    {
        for (var year = 2020; year <= 2060; year++)
        {
            FluentActions.Invoking(() => HolidayCalendar.ForYear(year, "SK")).Should().NotThrow();
            FluentActions.Invoking(() => HolidayCalendar.ForYear(year, "CZ")).Should().NotThrow();
        }
    }
}
