using AdhdTimeOrganizer.config.dependencyInjection;
using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.infrastructure.persistence.seeder.@interface;

namespace AdhdTimeOrganizer.infrastructure.persistence.seeder.dev;

public class WebExtensionDataSeeder(
    AppDbContext dbContext,
    ILogger<WebExtensionDataSeeder> logger) : IDevDatabaseSeeder, IScopedService
{
    public string SeederName => "WebExtensionData";
    public int Order => 12;

    public async Task TruncateTable()
    {
        await dbContext.TruncateTableAsync<WebExtensionData>();
    }

    public async Task SeedForUser(long userId)
    {
        var now = DateTime.UtcNow;
        var today = now.Date;

        // Create data for the last 7 days
        List<WebExtensionData> records = [];

        for (int daysAgo = 6; daysAgo >= 0; daysAgo--)
        {
            var date = today.AddDays(-daysAgo);

            // Morning session (9:00 - 12:00)
            records.AddRange(CreateSessionData(userId, date.AddHours(9), 3,
                [
                    ("github.com", "https://github.com/user/repo", 120, 60),
                    ("stackoverflow.com", "https://stackoverflow.com/questions/123", 90, 30),
                    ("docs.microsoft.com", "https://docs.microsoft.com/dotnet", 60, 15)
                ]));

            // Afternoon session (13:00 - 17:00)
            records.AddRange(CreateSessionData(userId, date.AddHours(13), 4,
                [
                    ("github.com", "https://github.com/user/repo/pulls", 180, 90),
                    ("localhost", "http://localhost:5000", 150, 60),
                    ("youtube.com", "https://youtube.com/watch?v=tutorial", 45, 15),
                    ("chatgpt.com", "https://chatgpt.com/chat", 90, 30)
                ]));

            // Evening session (19:00 - 21:00)
            records.AddRange(CreateSessionData(userId, date.AddHours(19), 2,
                [
                    ("reddit.com", "https://reddit.com/r/programming", 60, 45),
                    ("youtube.com", "https://youtube.com/watch?v=entertainment", 90, 30),
                    ("twitter.com", "https://twitter.com/home", 30, 20)
                ]));
        }

        await dbContext.WebExtensionData.AddRangeAsync(records);
        await dbContext.SaveChangesAsync();

        logger.LogInformation("Seeded {Count} web extension data records for user {UserId}", records.Count, userId);
    }

    private static List<WebExtensionData> CreateSessionData(
        long userId,
        DateTime sessionStart,
        int durationHours,
        List<(string Domain, string Url, int ActiveSeconds, int BackgroundSeconds)> activities)
    {
        var records = new List<WebExtensionData>();
        var windowCount = (durationHours * 60) / 5; // 5-minute windows

        for (int i = 0; i < windowCount; i++)
        {
            var windowStart = sessionStart.AddMinutes(i * 5);
            var isFinal = i == windowCount - 1;

            foreach (var (domain, url, activeSeconds, backgroundSeconds) in activities)
            {
                // Vary the activity slightly across windows
                var variance = Random.Shared.Next(-30, 30);
                var actualActive = Math.Max(0, activeSeconds + variance);
                var actualBackground = Math.Max(0, backgroundSeconds + variance / 2);

                // Only create records with actual activity
                if (actualActive > 0 || actualBackground > 0)
                {
                    records.Add(new WebExtensionData
                    {
                        UserId = userId,
                        WindowStart = windowStart,
                        Domain = domain,
                        Url = url,
                        ActiveSeconds = actualActive,
                        BackgroundSeconds = actualBackground,
                        IsFinal = isFinal
                    });
                }
            }
        }

        return records;
    }
}
