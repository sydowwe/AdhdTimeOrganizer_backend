using AdhdTimeOrganizer.domain.model.entity;
using AdhdTimeOrganizer.Core.domain.model.@enum;
using Sydowwe.Framework.infrastructure.persistence.seeder.@interface;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.infrastructure.persistence.seeder;

namespace AdhdTimeOrganizer.infrastructure.persistence.seeder.userDefault;

public class CalendarSeeder(
    AppDbContext dbContext,
    ILogger<CalendarSeeder> logger) : IScopedService, IPerUserDefaultSeeder
{
    public string SeederName => "Calendar";
    public int Order => 410;

    public async Task TruncateTable()
    {
        await dbContext.TruncateTableCascadeAsync<Calendar>();
    }

    public async Task SetupDefaults(long userId, CancellationToken ct = default)
    {
        var years = new[] { 2025, 2026 };
        var countryCode = "SK";

        foreach (var year in years)
            await SeedYearForUser(year, countryCode, userId, ct);

        logger.LogInformation("Completed seeding calendars for user {UserId} across years {Years}",
            userId, string.Join(", ", years));
    }

    /// <summary>
    /// Seeds calendar data for a specific year and country for a specific user
    /// </summary>
    /// <param name="year">Year to seed</param>
    /// <param name="countryCode">Country code (SK, CZ)</param>
    /// <param name="userId">User ID to associate calendars with</param>
    /// <param name="ct">Cancellation token</param>
    private async Task SeedYearForUser(int year, string countryCode, long userId, CancellationToken ct = default)
    {
        // Per missing date, not "the year has any rows at all": the unique index is (user_id, date),
        // so a year the user has partially deleted has to be filled in rather than skipped whole or
        // re-inserted whole.
        //
        // IgnoreQueryFilters for the same reason as BasePerUserDefaultSeeder: this reads the rows of
        // whichever user we were told to seed, which is not necessarily the one signed in, and the
        // host scopes IEntityWithUser reads to the ambient user. Filtered, it would read back no
        // dates for that user and re-insert the whole year onto (user_id, date).
        var existingDates = (await dbContext.Calendars
                .IgnoreQueryFilters()
                .Where(c => c.Date.Year == year && c.UserId == userId)
                .Select(c => c.Date)
                .ToListAsync(ct))
            .ToHashSet();

        var holidays = GetHolidays(year, countryCode);
        var calendars = new List<Calendar>();

        var startDate = new DateOnly(year, 1, 1);
        var endDate = new DateOnly(year, 12, 31);

        for (var date = startDate; date <= endDate; date = date.AddDays(1))
        {
            if (existingDates.Contains(date))
                continue;

            var dayOfWeek = date.DayOfWeek;
            var dayType = DayType.Workday;
            string? holidayName = null;

            // Check if it's a weekend
            if (dayOfWeek == DayOfWeek.Saturday || dayOfWeek == DayOfWeek.Sunday)
                dayType = DayType.Weekend;

            // Check if it's a holiday (holidays override weekends)
            if (holidays.TryGetValue(date, out var holiday))
                holidayName = holiday;

            var calendar = new Calendar
            {
                WakeUpTime = new TimeOnly(8, 0),
                BedTime = new TimeOnly(0, 0),
                Date = date,
                DayType = dayType,
                HolidayName = holidayName,
                UserId = userId
            };

            calendars.Add(calendar);
        }

        if (calendars.Count == 0)
        {
            logger.LogDebug("Calendar entries for year {Year} and user {UserId} already exist, skipping.", year, userId);
            return;
        }

        await dbContext.Calendars.AddRangeAsync(calendars, ct);
        await dbContext.SaveChangesAsync(ct);

        logger.LogInformation("Seeded {Count} calendar entries for year {Year} ({Country}) for user {UserId}",
            calendars.Count, year, countryCode, userId);
    }

    /// <summary>
    /// Public method to seed a specific year for a user (can be called manually)
    /// </summary>
    /// <param name="year">Year to seed</param>
    /// <param name="countryCode">Country code (SK, CZ)</param>
    /// <param name="userId">User ID to associate calendars with</param>
    public async Task SeedYear(int year, string countryCode, long userId)
    {
        await SeedYearForUser(year, countryCode, userId);
    }

    /// <summary>
    /// Gets holidays for a specific year and country
    /// </summary>
    private Dictionary<DateOnly, string> GetHolidays(int year, string countryCode)
    {
        return countryCode.ToUpper() switch
        {
            "SK" => GetSlovakHolidays(year),
            "CZ" => GetCzechHolidays(year),
            _ => new Dictionary<DateOnly, string>()
        };
    }

    /// <summary>
    /// Slovak public holidays
    /// </summary>
    private Dictionary<DateOnly, string> GetSlovakHolidays(int year)
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

        // Moveable holidays (Easter-based)
        var easterDate = CalculateEaster(year);

        // Good Friday (Veľký piatok) - 2 days before Easter
        holidays.Add(easterDate.AddDays(-2), "Veľký piatok");

        // Easter Monday (Veľkonočný pondelok) - 1 day after Easter
        holidays.Add(easterDate.AddDays(1), "Veľkonočný pondelok");

        return holidays;
    }

    /// <summary>
    /// Czech public holidays
    /// </summary>
    private Dictionary<DateOnly, string> GetCzechHolidays(int year)
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

        // Moveable holidays (Easter-based)
        var easterDate = CalculateEaster(year);

        // Good Friday (Velký pátek) - 2 days before Easter
        holidays.Add(easterDate.AddDays(-2), "Velký pátek");

        // Easter Monday (Velikonoční pondělí) - 1 day after Easter
        holidays.Add(easterDate.AddDays(1), "Velikonoční pondělí");

        return holidays;
    }

    /// <summary>
    /// Calculates Easter Sunday using Computus algorithm (Anonymous Gregorian algorithm)
    /// </summary>
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

    public async Task<bool> ResetDefaults(long userId, CancellationToken ct = default)
    {
        // Calendar seeder doesn't support reset - calendars are generated per year
        // and resetting them doesn't make sense as they are date-based
        logger.LogWarning("ResetDefaults is not supported for CalendarSeeder");
        return false;
    }
}