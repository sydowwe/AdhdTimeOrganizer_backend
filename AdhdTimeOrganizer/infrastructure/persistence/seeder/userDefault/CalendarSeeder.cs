using AdhdTimeOrganizer.domain.model.entity;
using AdhdTimeOrganizer.domain.model.@enum;
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
    public int Order => 5;

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
        var existingCount = await dbContext.Calendars
            .Where(c => c.Date.Year == year && c.UserId == userId)
            .CountAsync(ct);

        if (existingCount > 0)
        {
            logger.LogDebug("Calendar entries for year {Year} and user {UserId} already exist, skipping.", year, userId);
            return;
        }

        var holidays = GetHolidays(year, countryCode);
        var calendars = new List<Calendar>();

        var startDate = new DateOnly(year, 1, 1);
        var endDate = new DateOnly(year, 12, 31);

        for (var date = startDate; date <= endDate; date = date.AddDays(1))
        {
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
            { new DateOnly(year, 1, 1), "DeÅˆ vzniku Slovenskej republiky / NovÃ½ rok" },
            { new DateOnly(year, 1, 6), "Zjavenie PÃ¡na (Traja krÃ¡li)" },
            { new DateOnly(year, 5, 1), "Sviatok prÃ¡ce" },
            { new DateOnly(year, 5, 8), "DeÅˆ vÃ­Å¥azstva nad faÅ¡izmom" },
            { new DateOnly(year, 7, 5), "Sviatok svÃ¤tÃ©ho Cyrila a Metoda" },
            { new DateOnly(year, 8, 29), "VÃ½roÄie SNP" },
            { new DateOnly(year, 9, 1), "DeÅˆ Ãšstavy Slovenskej republiky" },
            { new DateOnly(year, 9, 15), "SedembolestnÃ¡ Panna MÃ¡ria" },
            { new DateOnly(year, 11, 1), "Sviatok VÅ¡etkÃ½ch svÃ¤tÃ½ch" },
            { new DateOnly(year, 11, 17), "DeÅˆ boja za slobodu a demokraciu" },
            { new DateOnly(year, 12, 24), "Å tedrÃ½ deÅˆ" },
            { new DateOnly(year, 12, 25), "PrvÃ½ sviatok vianoÄnÃ½" },
            { new DateOnly(year, 12, 26), "DruhÃ½ sviatok vianoÄnÃ½" }
        };

        // Moveable holidays (Easter-based)
        var easterDate = CalculateEaster(year);

        // Good Friday (VeÄ¾kÃ½ piatok) - 2 days before Easter
        holidays.Add(easterDate.AddDays(-2), "VeÄ¾kÃ½ piatok");

        // Easter Monday (VeÄ¾konoÄnÃ½ pondelok) - 1 day after Easter
        holidays.Add(easterDate.AddDays(1), "VeÄ¾konoÄnÃ½ pondelok");

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
            { new DateOnly(year, 1, 1), "Den obnovy samostatnÃ©ho ÄeskÃ©ho stÃ¡tu / NovÃ½ rok" },
            { new DateOnly(year, 5, 1), "SvÃ¡tek prÃ¡ce" },
            { new DateOnly(year, 5, 8), "Den vÃ­tÄ›zstvÃ­" },
            { new DateOnly(year, 7, 5), "Den slovanskÃ½ch vÄ›rozvÄ›stÅ¯ Cyrila a MetodÄ›je" },
            { new DateOnly(year, 7, 6), "Den upÃ¡lenÃ­ mistra Jana Husa" },
            { new DateOnly(year, 9, 28), "Den ÄeskÃ© stÃ¡tnosti" },
            { new DateOnly(year, 10, 28), "Den vzniku samostatnÃ©ho ÄeskoslovenskÃ©ho stÃ¡tu" },
            { new DateOnly(year, 11, 17), "Den boje za svobodu a demokracii" },
            { new DateOnly(year, 12, 24), "Å tÄ›drÃ½ den" },
            { new DateOnly(year, 12, 25), "1. svÃ¡tek vÃ¡noÄnÃ­" },
            { new DateOnly(year, 12, 26), "2. svÃ¡tek vÃ¡noÄnÃ­" }
        };

        // Moveable holidays (Easter-based)
        var easterDate = CalculateEaster(year);

        // Good Friday (VelkÃ½ pÃ¡tek) - 2 days before Easter
        holidays.Add(easterDate.AddDays(-2), "VelkÃ½ pÃ¡tek");

        // Easter Monday (VelikonoÄnÃ­ pondÄ›lÃ­) - 1 day after Easter
        holidays.Add(easterDate.AddDays(1), "VelikonoÄnÃ­ pondÄ›lÃ­");

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