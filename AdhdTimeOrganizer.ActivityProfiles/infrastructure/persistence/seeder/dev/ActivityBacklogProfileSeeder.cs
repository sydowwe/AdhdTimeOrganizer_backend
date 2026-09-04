using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.@enum;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.Core.domain.model.entity.@base;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.infrastructure.persistence.seeder;
using Sydowwe.Framework.infrastructure.persistence.seeder.@interface;

namespace AdhdTimeOrganizer.ActivityProfiles.infrastructure.persistence.seeder.dev;

public class ActivityBacklogProfileSeeder(
    DbContext dbContext,
    ILogger<ActivityBacklogProfileSeeder> logger) : IPerUserDevSeeder, IScopedService
{
    public string SeederName => "ActivityBacklogProfile";
    public int Order => 50;

    public async Task TruncateTable()
    {
        await dbContext.TruncateTableCascadeAsync<ActivityBacklogProfile>();
    }

    public async Task SeedForUser(long userId)
    {
        if (await dbContext.Set<ActivityBacklogProfile>().AnyAsync(p => p.Activity.UserId == userId))
            return;

        var activities = await dbContext.Set<Activity>()
            .Where(a => a.UserId == userId)
            .ToListAsync();

        if (activities.Count == 0)
        {
            logger.LogWarning("No activities found for user {UserId}, skipping ActivityBacklogProfile seeding", userId);
            return;
        }

        var locationIds = await LoadLookups<ActivityLocationType>(userId, ["Indoor", "Outdoor", "Any"]);
        var weatherIds = await LoadLookups<ActivityWeatherDependency>(userId, ["None", "Sunny", "Dry", "Snow"]);
        var costIds = await LoadLookups<ActivityExpectedCostTier>(userId, ["Free", "Cheap", "Moderate", "Expensive"]);

        if (locationIds == null || weatherIds == null || costIds == null)
            return;

        long? GetActivityId(string name)
        {
            return activities.FirstOrDefault(a => a.Name == name)?.Id;
        }

        var profiles = new List<ActivityBacklogProfile>();

        void Add(string name, string location, string weather, EnergyLevel energy,
            EffortType? effort, string cost, int minParticipants, int? maxParticipants,
            int durationMinutes, bool isRepeatable)
        {
            var activityId = GetActivityId(name);
            if (activityId == null)
                return;
            profiles.Add(new ActivityBacklogProfile
            {
                ActivityId = activityId.Value,
                LocationTypeId = locationIds[location],
                WeatherDependencyId = weatherIds[weather],
                EnergyLevel = energy,
                EffortType = effort,
                ExpectedCostTierId = costIds[cost],
                MinParticipants = minParticipants,
                MaxParticipants = maxParticipants,
                DurationMinutes = durationMinutes,
                IsRepeatable = isRepeatable
            });
        }

        Add("Gaming Session", "Indoor", "None", EnergyLevel.Low, EffortType.Mental, "Free", 1, null, 120, true);
        Add("Watch Movie/Series", "Indoor", "None", EnergyLevel.Low, EffortType.Mental, "Free", 1, null, 120, true);
        Add("Meditation", "Indoor", "None", EnergyLevel.Low, null, "Free", 1, 1, 30, true);
        Add("Journaling", "Indoor", "None", EnergyLevel.Medium, EffortType.Mental, "Free", 1, 1, 45, true);
        Add("Coffee with Friends", "Any", "None", EnergyLevel.Medium, null, "Cheap", 2, 6, 90, true);
        Add("Read Technical Book", "Indoor", "None", EnergyLevel.Medium, EffortType.Mental, "Free", 1, null, 60, false);
        Add("Online Course", "Indoor", "Snow", EnergyLevel.High, EffortType.Mental, "Expensive", 1, null, 90, true);
        Add("Grocery Shopping", "Outdoor", "Sunny", EnergyLevel.Medium, EffortType.Physical, "Moderate", 1, 2, 60, true);
        Add("Morning Exercise", "Outdoor", "Dry", EnergyLevel.High, EffortType.Physical, "Free", 1, null, 60, true);
        Add("Video Call Parents", "Any", "None", EnergyLevel.Low, null, "Free", 2, 5, 45, true);

        await dbContext.Set<ActivityBacklogProfile>().AddRangeAsync(profiles);
        await dbContext.SaveChangesAsync();

        logger.LogInformation("Seeded {Count} backlog profiles for user {UserId}", profiles.Count, userId);
    }

    /// <summary>
    /// Reads the lookup rows the <c>userDefault/</c> seeders own (orders 20-23, which run before this one at 50).
    /// It never creates them: those seeders set columns this one cannot see — <c>ActivityWeatherDependency.Code</c>
    /// above all — so a row invented here would be a subtly broken copy. Returns <c>null</c> when anything is
    /// missing, which means the default seeders did not run and there is nothing sane to seed against.
    /// </summary>
    private async Task<Dictionary<string, long>?> LoadLookups<T>(long userId, string[] texts)
        where T : BaseLookupWithUser
    {
        var existing = await dbContext.Set<T>()
            .Where(l => l.UserId == userId && texts.Contains(l.Text))
            .ToDictionaryAsync(l => l.Text, l => l.Id);

        var missing = texts.Where(t => !existing.ContainsKey(t)).ToList();
        if (missing.Count == 0)
            return existing;

        logger.LogWarning(
            "Missing {Lookup} defaults {Missing} for user {UserId}, skipping ActivityBacklogProfile seeding",
            typeof(T).Name, string.Join(", ", missing), userId);
        return null;
    }
}