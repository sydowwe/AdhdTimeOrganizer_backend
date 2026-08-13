namespace AdhdTimeOrganizer.Planning.domain.service;

/// <summary>
/// Public holidays by year and country. Lifted out of <c>CalendarSeeder</c>, which used to be the only thing
/// that knew them — fine while whole years were seeded up front, wrong once a calendar row can also be created
/// lazily on first task. A second copy of this table would mean a day planned in advance and the same day
/// planned on the spot disagree about whether it is a holiday.
/// </summary>
public static class HolidayCalendar
{
    /// <summary>The only country this app has ever seeded. One place, so the two callers cannot drift.</summary>
    public const string DefaultCountryCode = "SK";

    public static Dictionary<DateOnly, string> ForYear(int year, string countryCode = DefaultCountryCode)
    {
        return countryCode.ToUpperInvariant() switch
        {
            "SK" => Slovak(year),
            "CZ" => Czech(year),
            _ => []
        };
    }

    /// <summary>The holiday falling on <paramref name="date"/>, or null. For one date, prefer this over
    /// building the whole year.</summary>
    public static string? ForDate(DateOnly date, string countryCode = DefaultCountryCode)
    {
        return ForYear(date.Year, countryCode).GetValueOrDefault(date);
    }

    private static Dictionary<DateOnly, string> Slovak(int year)
    {
        var holidays = new Dictionary<DateOnly, string>
        {
            // Fixed holidays
            { new DateOnly(year, 1, 1), "Deň vzniku Slovenskej republiky / Nový rok" },
            { new DateOnly(year, 1, 6), "Zjavenie Pána (Traja králi)" },
            { new DateOnly(year, 5, 1), "Sviatok práce" },
            { new DateOnly(year, 5, 8), "Deň víťazstva nad fašizmom" },
            { new DateOnly(year, 7, 5), "Sviatok svätého Cyrila a Metoda" },
            { new DateOnly(year, 8, 29), "Výročie SNP" },
            { new DateOnly(year, 9, 1), "Deň Ústavy Slovenskej republiky" },
            { new DateOnly(year, 9, 15), "Sedembolestná Panna Mária" },
            { new DateOnly(year, 11, 1), "Sviatok Všetkých svätých" },
            { new DateOnly(year, 11, 17), "Deň boja za slobodu a demokraciu" },
            { new DateOnly(year, 12, 24), "Štedrý deň" },
            { new DateOnly(year, 12, 25), "Prvý sviatok vianočný" },
            { new DateOnly(year, 12, 26), "Druhý sviatok vianočný" }
        };

        var easter = CalculateEaster(year);
        holidays.Add(easter.AddDays(-2), "Veľký piatok");
        holidays.Add(easter.AddDays(1), "Veľkonočný pondelok");

        return holidays;
    }

    private static Dictionary<DateOnly, string> Czech(int year)
    {
        var holidays = new Dictionary<DateOnly, string>
        {
            // Fixed holidays
            { new DateOnly(year, 1, 1), "Den obnovy samostatného českého státu / Nový rok" },
            { new DateOnly(year, 5, 1), "Svátek práce" },
            { new DateOnly(year, 5, 8), "Den vítězství" },
            { new DateOnly(year, 7, 5), "Den slovanských věrozvěstů Cyrila a Metoděje" },
            { new DateOnly(year, 7, 6), "Den upálení mistra Jana Husa" },
            { new DateOnly(year, 9, 28), "Den české státnosti" },
            { new DateOnly(year, 10, 28), "Den vzniku samostatného československého státu" },
            { new DateOnly(year, 11, 17), "Den boje za svobodu a demokracii" },
            { new DateOnly(year, 12, 24), "Štědrý den" },
            { new DateOnly(year, 12, 25), "1. svátek vánoční" },
            { new DateOnly(year, 12, 26), "2. svátek vánoční" }
        };

        var easter = CalculateEaster(year);
        holidays.Add(easter.AddDays(-2), "Velký pátek");
        holidays.Add(easter.AddDays(1), "Velikonoční pondělí");

        return holidays;
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
