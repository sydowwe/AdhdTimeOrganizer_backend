using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.infrastructure.persistence.seeder.@interface;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.domain.valueObject;
using Sydowwe.Framework.infrastructure.persistence.seeder;

namespace AdhdTimeOrganizer.History.infrastructure.persistence.seeder.dev;

public class ActivityHistorySeeder(
    DbContext dbContext,
    ILogger<ActivityHistorySeeder> logger) : IScopedService, IPerUserDevSeeder
{
    public string SeederName => "ActivityHistory";
    public int Order => 300;

    public async Task TruncateTable()
    {
        await dbContext.TruncateTableCascadeAsync<ActivityHistory>();
    }

    public async Task SeedForUser(long userId)
    {
        var activities = dbContext.Set<Activity>()
            .Where(a => a.UserId == userId)
            .ToList();

        if (activities.Count == 0)
        {
            logger.LogWarning("No activities found for user {UserId}, skipping ActivityHistory seeding", userId);
            return;
        }

        var random = new Random(42);
        var now = DateTime.UtcNow;
        var activityHistories = new List<ActivityHistory>();

        // Seed history entries for the past 30 days
        for (var day = 30; day >= 0; day--)
        {
            var date = now.Date.AddDays(-day);
            var currentTime = date.AddHours(7); // Start at 7 AM

            // 3-6 activity entries per day
            var entriesPerDay = random.Next(3, 7);

            for (var i = 0; i < entriesPerDay; i++)
            {
                var activity = activities[random.Next(activities.Count)];
                var durationMinutes = random.Next(1, 5) * 15; // 15, 30, 45, or 60 minutes
                var length = new IntTime(0, durationMinutes);

                var startTimestamp = currentTime;
                var endTimestamp = startTimestamp.AddMinutes(durationMinutes);

                activityHistories.Add(new ActivityHistory
                {
                    StartTimestamp = startTimestamp,
                    Length = length,
                    EndTimestamp = endTimestamp,
                    ActivityId = activity.Id,
                    UserId = userId
                });

                // Gap between activities (15-60 min)
                currentTime = endTimestamp.AddMinutes(random.Next(1, 5) * 15);
            }
        }

        await dbContext.Set<ActivityHistory>().AddRangeAsync(activityHistories);
        await dbContext.SaveChangesAsync();

        logger.LogInformation("Seeded {Count} activity history entries for user {UserId}", activityHistories.Count, userId);
    }
}