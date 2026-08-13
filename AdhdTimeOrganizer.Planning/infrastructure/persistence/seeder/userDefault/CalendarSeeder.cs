using AdhdTimeOrganizer.Planning.domain.model.entity;
using AdhdTimeOrganizer.Planning.domain.service;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.infrastructure.persistence.seeder;
using Sydowwe.Framework.infrastructure.persistence.seeder.@interface;

namespace AdhdTimeOrganizer.Planning.infrastructure.persistence.seeder.userDefault;

public class CalendarSeeder(
    DbContext dbContext,
    ILogger<CalendarSeeder> logger) : IScopedService, IPerUserDefaultSeeder
{
    public string SeederName => "Calendar";
    public int Order => 410;

    public async Task TruncateTable()
    {
        await dbContext.TruncateTableCascadeAsync<Calendar>();
    }

    /// <summary>
    /// This year and next, resolved when the seeder runs rather than hard-coded. It used to say
    /// <c>{ 2025, 2026 }</c>, which quietly set an expiry date on the whole planner: past the last seeded year
    /// every date resolved to no calendar row, and with no create-calendar endpoint nothing in the app could
    /// make one. Lazy creation (<c>CalendarProvisioner</c>) is what actually fixes that; a rolling window is
    /// just no longer walking into it on purpose.
    /// </summary>
    private static int[] SeedYears()
    {
        var thisYear = DateTime.UtcNow.Year;
        return [thisYear, thisYear + 1];
    }

    public async Task SetupDefaults(long userId, CancellationToken ct = default)
    {
        var years = SeedYears();
        var countryCode = HolidayCalendar.DefaultCountryCode;

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
        var existingDates = (await dbContext.Set<Calendar>()
                .IgnoreQueryFilters()
                .Where(c => c.Date.Year == year && c.UserId == userId)
                .Select(c => c.Date)
                .ToListAsync(ct))
            .ToHashSet();

        var calendars = new List<Calendar>();

        var startDate = new DateOnly(year, 1, 1);
        var endDate = new DateOnly(year, 12, 31);

        for (var date = startDate; date <= endDate; date = date.AddDays(1))
        {
            if (existingDates.Contains(date))
                continue;

            // Day type, holiday name and the default sleep window all come from CalendarDayFactory, which
            // CalendarProvisioner also uses. A day filled in here and the same day created lazily on its first
            // task have to be the same day.
            calendars.Add(CalendarDayFactory.Create(userId, date, countryCode));
        }

        if (calendars.Count == 0)
        {
            logger.LogDebug("Calendar entries for year {Year} and user {UserId} already exist, skipping.", year, userId);
            return;
        }

        await dbContext.Set<Calendar>().AddRangeAsync(calendars, ct);
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

    public async Task<bool> ResetDefaults(long userId, CancellationToken ct = default)
    {
        // Calendar seeder doesn't support reset - calendars are generated per year
        // and resetting them doesn't make sense as they are date-based
        logger.LogWarning("ResetDefaults is not supported for CalendarSeeder");
        return false;
    }
}