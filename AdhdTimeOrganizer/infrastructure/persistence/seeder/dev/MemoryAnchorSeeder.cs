using AdhdTimeOrganizer.config.dependencyInjection;
using AdhdTimeOrganizer.domain.model.entity.activity.memoryAnchor;
using AdhdTimeOrganizer.domain.model.entity.activity.profile;
using AdhdTimeOrganizer.infrastructure.persistence.seeder.@interface;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.infrastructure.persistence.seeder.dev;

public class MemoryAnchorSeeder(
    AppDbContext dbContext,
    ILogger<MemoryAnchorSeeder> logger) : IDevDatabaseSeeder, IScopedService
{
    public string SeederName => "MemoryAnchor";
    public int Order => 13;

    public async Task TruncateTable()
    {
        await dbContext.TruncateTableAsync<MemoryAnchor>();
    }

    public async Task SeedForUser(long userId)
    {
        if (await dbContext.Set<MemoryAnchor>().AnyAsync(ma => ma.UserId == userId))
            return;

        var userActivityIds = await dbContext.Activities
            .Where(a => a.UserId == userId)
            .Select(a => a.Id)
            .ToListAsync();

        if (userActivityIds.Count == 0)
        {
            logger.LogWarning("No activities found for user {UserId}, skipping MemoryAnchor seeding", userId);
            return;
        }

        var backlogActivityIds = await dbContext.Set<ActivityBacklogProfile>()
            .Where(p => userActivityIds.Contains(p.ActivityId))
            .Select(p => p.ActivityId)
            .ToListAsync();

        var bucketListActivityIds = await dbContext.Set<ActivityBucketListProfile>()
            .Where(p => userActivityIds.Contains(p.ActivityId))
            .Select(p => p.ActivityId)
            .ToListAsync();

        var eligibleIds = backlogActivityIds.Union(bucketListActivityIds).ToList();

        if (eligibleIds.Count == 0)
        {
            logger.LogWarning("No backlog or bucket list profiles found for user {UserId}, skipping MemoryAnchor seeding", userId);
            return;
        }

        var random = new Random(42);
        var today = new DateTime(2026, 5, 19);
        var used = new HashSet<(long, int, int)>();

        string[] notes =
        [
            "Finally managed to do this consistently — felt great!",
            "Struggled at first but found my rhythm",
            "Best session yet, very productive",
            "Surprised how much I enjoyed this",
            "Need to do this more often",
            "Felt energized afterward",
            "This became a highlight of the month",
            "Challenging but rewarding experience",
            "Made real progress on my goals",
            "Memorable moment worth repeating",
            "Found unexpected joy in the process",
            "Pushed past my comfort zone today",
            "Connected with something important to me",
            "Saw tangible results for the first time",
            "This changed my perspective",
            "Flow state — lost track of time completely",
            "Shared the experience with someone special",
            "Overcame a mental block I had been carrying",
            "Felt proud of myself for following through",
            "Would give this a perfect score any day"
        ];

        var anchors = new List<MemoryAnchor>();

        foreach (var activityId in eligibleIds)
        {
            var anchorCount = random.Next(1, 3); // 1 or 2 per activity

            for (var i = 0; i < anchorCount; i++)
            {
                // Pick a month within the last 12 months; retry once on collision
                var monthsAgo = random.Next(0, 12);
                var anchorDate = today.AddMonths(-monthsAgo);

                if (used.Contains((activityId, anchorDate.Month, anchorDate.Year)))
                {
                    var alt = (monthsAgo + 2) % 12;
                    anchorDate = today.AddMonths(-alt);
                    if (used.Contains((activityId, anchorDate.Month, anchorDate.Year)))
                        continue;
                }

                used.Add((activityId, anchorDate.Month, anchorDate.Year));

                anchors.Add(new MemoryAnchor
                {
                    ActivityId = activityId,
                    UserId = userId,
                    AnchorMonth = anchorDate.Month,
                    AnchorYear = anchorDate.Year,
                    HighlightNote = notes[random.Next(notes.Length)],
                    Rating = random.Next(1, 11)
                });
            }
        }

        await dbContext.Set<MemoryAnchor>().AddRangeAsync(anchors);
        await dbContext.SaveChangesAsync();

        logger.LogInformation("Seeded {Count} memory anchors for user {UserId}", anchors.Count, userId);
    }
}
