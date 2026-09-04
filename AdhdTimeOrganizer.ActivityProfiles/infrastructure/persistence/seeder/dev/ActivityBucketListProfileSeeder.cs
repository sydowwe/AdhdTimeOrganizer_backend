using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.Core.domain.model.entity.@base;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.infrastructure.persistence.seeder;
using Sydowwe.Framework.infrastructure.persistence.seeder.@interface;

namespace AdhdTimeOrganizer.ActivityProfiles.infrastructure.persistence.seeder.dev;

public class ActivityBucketListProfileSeeder(
    DbContext dbContext,
    ILogger<ActivityBucketListProfileSeeder> logger) : IPerUserDevSeeder, IScopedService
{
    public string SeederName => "ActivityBucketListProfile";
    public int Order => 52;

    public async Task TruncateTable()
    {
        await dbContext.TruncateTableCascadeAsync<ActivityBucketListProfile>();
    }

    public async Task SeedForUser(long userId)
    {
        if (await dbContext.Set<ActivityBucketListProfile>().AnyAsync(p => p.Activity.UserId == userId))
            return;

        var activities = await dbContext.Set<Activity>()
            .Where(a => a.UserId == userId)
            .ToListAsync();

        if (activities.Count == 0)
        {
            logger.LogWarning("No activities found for user {UserId}, skipping ActivityBucketListProfile seeding", userId);
            return;
        }

        var experienceIds = await LoadLookups<ActivityExperienceType>(userId,
            ["Adrenaline", "Travel", "Skill", "Culinary", "Cultural"]);

        if (experienceIds == null)
            return;

        long? GetActivityId(string name)
        {
            return activities.FirstOrDefault(a => a.Name == name)?.Id;
        }

        var profiles = new List<ActivityBucketListProfile>();

        void Add(string name, string experienceType, int comfortZoneStep,
            bool requiresTravel, decimal? financialGoal, string inspirationSource)
        {
            var activityId = GetActivityId(name);
            if (activityId == null)
                return;
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

    /// <summary>
    /// Reads the lookup rows the <c>userDefault/</c> seeders own (order 23, which runs before this one at 52).
    /// It never creates them — inventing a row here would bypass whatever columns those seeders set — and
    /// returns <c>null</c> when anything is missing, which means the default seeders did not run.
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
            "Missing {Lookup} defaults {Missing} for user {UserId}, skipping ActivityBucketListProfile seeding",
            typeof(T).Name, string.Join(", ", missing), userId);
        return null;
    }
}