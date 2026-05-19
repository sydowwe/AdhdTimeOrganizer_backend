using AdhdTimeOrganizer.config.dependencyInjection;
using AdhdTimeOrganizer.domain.model.entity.activity.lookup;
using AdhdTimeOrganizer.domain.model.entity.activity.profile;
using AdhdTimeOrganizer.domain.model.entity.@base.core;
using AdhdTimeOrganizer.infrastructure.persistence.seeder.@interface;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.infrastructure.persistence.seeder.dev;

public class ActivityBucketListProfileSeeder(
    AppDbContext dbContext,
    ILogger<ActivityBucketListProfileSeeder> logger) : IDevDatabaseSeeder, IScopedService
{
    public string SeederName => "ActivityBucketListProfile";
    public int Order => 12;

    public async Task TruncateTable()
    {
        await dbContext.TruncateTableAsync<ActivityBucketListProfile>();
    }

    public async Task SeedForUser(long userId)
    {
        if (await dbContext.Set<ActivityBucketListProfile>().AnyAsync())
            return;

        var activities = await dbContext.Activities
            .Where(a => a.UserId == userId)
            .ToListAsync();

        if (activities.Count == 0)
        {
            logger.LogWarning("No activities found for user {UserId}, skipping ActivityBucketListProfile seeding", userId);
            return;
        }

        var experienceIds = await EnsureLookups<ActivityExperienceType>(userId,
            [("Adrenaline", 1), ("Travel", 2), ("Skill", 3), ("Culinary", 4), ("Cultural", 5)]);

        long? GetActivityId(string name) => activities.FirstOrDefault(a => a.Name == name)?.Id;

        var profiles = new List<ActivityBucketListProfile>();

        void Add(string name, string experienceType, int comfortZoneStep,
            bool requiresTravel, decimal? financialGoal, string inspirationSource)
        {
            var activityId = GetActivityId(name);
            if (activityId == null) return;
            profiles.Add(new ActivityBucketListProfile
            {
                ActivityId = activityId.Value,
                ExperienceTypeId = experienceIds[experienceType],
                ComfortZoneStep = comfortZoneStep,
                RequiresTravel = requiresTravel,
                FinancialGoal = financialGoal,
                InspirationSource = inspirationSource
            });
        }

        Add("Family Dinner",
            "Culinary", 2, false, null,
            "Recreate grandmother's traditional recipes for the whole family");

        Add("Code Review",
            "Travel", 1, true, 1500m,
            "Participate in an in-person open-source hackathon abroad");

        Add("Doctor Appointment",
            "Adrenaline", 3, false, null,
            "Overcome health anxiety by completing a full annual medical checkup");

        Add("Sprint Planning",
            "Cultural", 5, false, null,
            "Master agile ceremony facilitation and team coordination at scale");

        Add("Feature Development",
            "Skill", 4, true, 3000m,
            "Ship a meaningful product and present it at a major tech conference");

        await dbContext.Set<ActivityBucketListProfile>().AddRangeAsync(profiles);
        await dbContext.SaveChangesAsync();

        logger.LogInformation("Seeded {Count} bucket list profiles for user {UserId}", profiles.Count, userId);
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
