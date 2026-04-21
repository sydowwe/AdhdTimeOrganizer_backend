using AdhdTimeOrganizer.config.dependencyInjection;
using AdhdTimeOrganizer.domain.helper;
using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.infrastructure.persistence.seeder.@interface;

namespace AdhdTimeOrganizer.infrastructure.persistence.seeder.dev;

public class ActivityHistorySeeder(
    AppDbContext dbContext,
    ILogger<ActivityHistorySeeder> logger) : IScopedService, IDevDatabaseSeeder
{
    public string SeederName => "ActivityHistory";
    public int Order => 14;

    public async Task TruncateTable()
    {
        await dbContext.TruncateTableAsync<ActivityHistory>();
    }

    public async Task SeedForUser(long userId)
    {
        var activities = dbContext.Activities
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

        await dbContext.ActivityHistories.AddRangeAsync(activityHistories);
        await dbContext.SaveChangesAsync();

        logger.LogInformation("Seeded {Count} activity history entries for user {UserId}", activityHistories.Count, userId);
    }
}
