namespace AdhdTimeOrganizer.Planning.domain.service;

/// <summary>
/// One public holiday: the stable key a client translates through its locale table
/// (<c>calendar.holidays.&lt;Key&gt;</c>), and the native Slovak name the day actually has.
/// </summary>
/// <param name="Key">Locale-independent identifier — <c>SCREAMING_SNAKE</c>, never shown to a user as-is,
/// never renamed once a calendar row has been written with it.</param>
/// <param name="Name">The proper name in Slovak, kept alongside the key so a reader without a locale
/// table (the history calendar, a raw row) still has something human to show.</param>
public sealed record Holiday(string Key, string Name);

/// <summary>
/// Slovak public holidays by year. Lifted out of <c>CalendarSeeder</c>, which used to be the only thing that
/// knew them — fine while whole years were seeded up front, wrong once a calendar row can also be created
/// lazily on first task. A second copy of this table would mean a day planned in advance and the same day
/// planned on the spot disagree about whether it is a holiday.
/// <para>
/// Slovak only, by decision (B19): the app has never seeded another country, <c>User</c> carries no country,
/// and localisation happens on the client off <see cref="Holiday.Key"/> rather than by keeping a second
/// table per language. The Czech table that used to sit beside this one went with that decision.
/// </para>
/// </summary>
public static class HolidayCalendar
{
    public static Dictionary<DateOnly, Holiday> ForYear(int year)
    {
        var holidays = new Dictionary<DateOnly, Holiday>
        {
            // Fixed holidays
            { new DateOnly(year, 1, 1), new Holiday("NEW_YEAR", "Deň vzniku Slovenskej republiky / Nový rok") },
            { new DateOnly(year, 1, 6), new Holiday("EPIPHANY", "Zjavenie Pána (Traja králi)") },
            { new DateOnly(year, 5, 1), new Holiday("LABOUR_DAY", "Sviatok práce") },
            { new DateOnly(year, 5, 8), new Holiday("VICTORY_DAY", "Deň víťazstva nad fašizmom") },
            { new DateOnly(year, 7, 5), new Holiday("CYRIL_AND_METHODIUS", "Sviatok svätého Cyrila a Metoda") },
            { new DateOnly(year, 8, 29), new Holiday("SNP_ANNIVERSARY", "Výročie SNP") },
            { new DateOnly(year, 9, 1), new Holiday("CONSTITUTION_DAY", "Deň Ústavy Slovenskej republiky") },
            { new DateOnly(year, 9, 15), new Holiday("OUR_LADY_OF_SORROWS", "Sedembolestná Panna Mária") },
            { new DateOnly(year, 11, 1), new Holiday("ALL_SAINTS", "Sviatok Všetkých svätých") },
            { new DateOnly(year, 11, 17), new Holiday("FREEDOM_AND_DEMOCRACY_DAY", "Deň boja za slobodu a demokraciu") },
            { new DateOnly(year, 12, 24), new Holiday("CHRISTMAS_EVE", "Štedrý deň") },
            { new DateOnly(year, 12, 25), new Holiday("CHRISTMAS_DAY", "Prvý sviatok vianočný") },
            { new DateOnly(year, 12, 26), new Holiday("ST_STEPHENS_DAY", "Druhý sviatok vianočný") }
        };

        var easter = CalculateEaster(year);
        holidays.Add(easter.AddDays(-2), new Holiday("GOOD_FRIDAY", "Veľký piatok"));
        holidays.Add(easter.AddDays(1), new Holiday("EASTER_MONDAY", "Veľkonočný pondelok"));

        return holidays;
    }

    /// <summary>The holiday falling on <paramref name="date"/>, or null. For one date, prefer this over
    /// building the whole year.</summary>
    public static Holiday? ForDate(DateOnly date)
    {
        return ForYear(date.Year).GetValueOrDefault(date);
    }

    /// <summary>Easter Sunday, by the anonymous Gregorian Computus.</summary>
    private static DateOnly CalculateEaster(int year)
    {
        var a = year % 19;
        var b = year / 100;
        var c = year % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = (19 * a + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + 2 * e + 2 * i - h - k) % 7;
        var m = (a + 11 * h + 22 * l) / 451;
        var month = (h + l - 7 * m + 114) / 31;
        var day = (h + l - 7 * m + 114) % 31 + 1;

        return new DateOnly(year, month, day);
    }
}
