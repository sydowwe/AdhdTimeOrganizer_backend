using AdhdTimeOrganizer.config.dependencyInjection;
using AdhdTimeOrganizer.domain.model.entity.activity.lookup;
using AdhdTimeOrganizer.domain.model.entity.activity.profile;
using AdhdTimeOrganizer.domain.model.entity.@base.core;
using AdhdTimeOrganizer.domain.model.@enum;
using AdhdTimeOrganizer.infrastructure.persistence.seeder.@interface;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.infrastructure.persistence.seeder.dev;

public class ActivityBacklogProfileSeeder(
    AppDbContext dbContext,
    ILogger<ActivityBacklogProfileSeeder> logger) : IDevDatabaseSeeder, IScopedService
{
    public string SeederName => "ActivityBacklogProfile";
    public int Order => 10;

    public async Task TruncateTable()
    {
        await dbContext.TruncateTableAsync<ActivityBacklogProfile>();
    }

    public async Task SeedForUser(long userId)
    {
        if (await dbContext.Set<ActivityBacklogProfile>().AnyAsync())
            return;

        var activities = await dbContext.Activities
            .Where(a => a.UserId == userId)
            .ToListAsync();

        if (activities.Count == 0)
        {
            logger.LogWarning("No activities found for user {UserId}, skipping ActivityBacklogProfile seeding", userId);
            return;
        }

        var locationIds = await EnsureLookups<ActivityLocationType>(userId,
            [("Indoor", 1), ("Outdoor", 2), ("Any", 3)]);
        var weatherIds = await EnsureLookups<ActivityWeatherDependency>(userId,
            [("None", 1), ("Sunny", 2), ("Dry", 3), ("Snow", 4)]);
        var costIds = await EnsureLookups<ActivityExpectedCostTier>(userId,
            [("Free", 1), ("Cheap", 2), ("Moderate", 3), ("Expensive", 4)]);

        long? GetActivityId(string name) => activities.FirstOrDefault(a => a.Name == name)?.Id;

        var profiles = new List<ActivityBacklogProfile>();

        void Add(string name, string location, string weather, EnergyLevel energy,
            EffortType? effort, string cost, int minParticipants, int? maxParticipants,
            int durationMinutes, bool isRepeatable)
        {
            var activityId = GetActivityId(name);
            if (activityId == null) return;
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

        Add("Gaming Session",      "Indoor",  "None",  EnergyLevel.Low,    EffortType.Mental,   "Free",     1, null, 120, true);
        Add("Watch Movie/Series",  "Indoor",  "None",  EnergyLevel.Low,    EffortType.Mental,   "Free",     1, null, 120, true);
        Add("Meditation",          "Indoor",  "None",  EnergyLevel.Low,    null,                "Free",     1, 1,   30,  true);
        Add("Journaling",          "Indoor",  "None",  EnergyLevel.Medium, EffortType.Mental,   "Free",     1, 1,   45,  true);
        Add("Coffee with Friends", "Any",     "None",  EnergyLevel.Medium, null,                "Cheap",    2, 6,   90,  true);
        Add("Read Technical Book", "Indoor",  "None",  EnergyLevel.Medium, EffortType.Mental,   "Free",     1, null, 60,  false);
        Add("Online Course",       "Indoor",  "Snow",  EnergyLevel.High,   EffortType.Mental,   "Expensive",1, null, 90,  true);
        Add("Grocery Shopping",    "Outdoor", "Sunny", EnergyLevel.Medium, EffortType.Physical, "Moderate", 1, 2,   60,  true);
        Add("Morning Exercise",    "Outdoor", "Dry",   EnergyLevel.High,   EffortType.Physical, "Free",     1, null, 60,  true);
        Add("Video Call Parents",  "Any",     "None",  EnergyLevel.Low,    null,                "Free",     2, 5,   45,  true);

        await dbContext.Set<ActivityBacklogProfile>().AddRangeAsync(profiles);
        await dbContext.SaveChangesAsync();

        logger.LogInformation("Seeded {Count} backlog profiles for user {UserId}", profiles.Count, userId);
    }

    private async Task<Dictionary<string, long>> EnsureLookups<T>(long userId, (string Text, int SortOrder)[] entries)
        where T : BaseLookup
    {
        var existing = await dbContext.Set<T>()
            .Where(l => l.UserId == userId)
            .ToDictionaryAsync(l => l.Text, l => l.Id);

        foreach (var (text, sortOrder) in entries)
        {
            if (existing.ContainsKey(text)) continue;
            var entity = (T)Activator.CreateInstance(typeof(T))!;
            entity.Text = text;
            entity.SortOrder = sortOrder;
            entity.UserId = userId;
            dbContext.Set<T>().Add(entity);
            await dbContext.SaveChangesAsync();
            existing[text] = entity.Id;
        }

        return existing;
    }
}
