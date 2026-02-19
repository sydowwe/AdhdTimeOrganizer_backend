using AdhdTimeOrganizer.config.dependencyInjection;
using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.infrastructure.persistence.seeder.@interface;
using Microsoft.EntityFrameworkCore;

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
        // Skip if already seeded (guard against duplicate DI resolution)
        if (await dbContext.WebExtensionData.IgnoreQueryFilters().AnyAsync(x => x.UserId == userId))
            return;

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
                    ("github.com", "https://github.com/user/repo", 40, 20),
                    ("stackoverflow.com", "https://stackoverflow.com/questions/123", 30, 10),
                    ("docs.microsoft.com", "https://docs.microsoft.com/dotnet", 20, 5)
                ]));

            // Afternoon session (13:00 - 17:00)
            records.AddRange(CreateSessionData(userId, date.AddHours(13), 4,
                [
                    ("github.com", "https://github.com/user/repo/pulls", 45, 15),
                    ("localhost", "http://localhost:5000", 40, 20),
                    ("youtube.com", "https://youtube.com/watch?v=tutorial", 15, 5),
                    ("chatgpt.com", "https://chatgpt.com/chat", 30, 10)
                ]));

            // Evening session (19:00 - 21:00)
            records.AddRange(CreateSessionData(userId, date.AddHours(19), 2,
                [
                    ("reddit.com", "https://reddit.com/r/programming", 25, 15),
                    ("youtube.com", "https://youtube.com/watch?v=entertainment", 35, 10),
                    ("twitter.com", "https://twitter.com/home", 15, 10)
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
        List<(string Domain, string Url, int ActiveWeight, int BackgroundWeight)> activities)
    {
        var records = new List<WebExtensionData>();
        var windowCount = durationHours * 60; // 1-minute windows
        var totalActiveWeight = activities.Sum(a => a.ActiveWeight);
        var lastActiveDomainIndex = -1;

        for (int i = 0; i < windowCount; i++)
        {
            var windowStart = sessionStart.AddMinutes(i);
            var isFinal = i == windowCount - 1;
            var remainingActive = 60;

            // 80% chance to stay on the same domain (continuity), otherwise pick by weight
            int primaryIndex;
            if (lastActiveDomainIndex >= 0 && Random.Shared.Next(100) < 80)
            {
                primaryIndex = lastActiveDomainIndex;
            }
            else
            {
                var roll = Random.Shared.Next(totalActiveWeight);
                var cumulative = 0;
                primaryIndex = 0;
                for (int j = 0; j < activities.Count; j++)
                {
                    cumulative += activities[j].ActiveWeight;
                    if (roll < cumulative) { primaryIndex = j; break; }
                }
            }

            lastActiveDomainIndex = primaryIndex;

            var primaryActive = Random.Shared.Next(25, 56);
            remainingActive -= primaryActive;

            records.Add(new WebExtensionData
            {
                UserId = userId,
                RecordDate = DateOnly.FromDateTime(windowStart),
                WindowStart = windowStart,
                Domain = activities[primaryIndex].Domain,
                Url = activities[primaryIndex].Url,
                ActiveSeconds = primaryActive,
                BackgroundSeconds = 0,
                IsFinal = isFinal
            });

            // Track used domain indices to avoid duplicate keys
            var usedIndices = new HashSet<int> { primaryIndex };

            // ~15% chance of a second active domain (tab switch mid-window)
            if (remainingActive > 5 && Random.Shared.Next(100) < 15)
            {
                var secondIndex = (primaryIndex + Random.Shared.Next(1, activities.Count)) % activities.Count;
                var secondActive = Random.Shared.Next(5, Math.Min(remainingActive + 1, 25));
                usedIndices.Add(secondIndex);

                records.Add(new WebExtensionData
                {
                    UserId = userId,
                    RecordDate = DateOnly.FromDateTime(windowStart),
                    WindowStart = windowStart,
                    Domain = activities[secondIndex].Domain,
                    Url = activities[secondIndex].Url,
                    ActiveSeconds = secondActive,
                    BackgroundSeconds = 0,
                    IsFinal = isFinal
                });
            }

            // Max 2 background domains (~60% of windows)
            if (Random.Shared.Next(100) >= 60) continue;

            var bgCandidates = activities
                .Where((_, idx) => !usedIndices.Contains(idx))
                .OrderBy(_ => Random.Shared.Next())
                .Take(2);

            foreach (var bg in bgCandidates)
            {
                records.Add(new WebExtensionData
                {
                    UserId = userId,
                    RecordDate = DateOnly.FromDateTime(windowStart),
                    WindowStart = windowStart,
                    Domain = bg.Domain,
                    Url = bg.Url,
                    ActiveSeconds = 0,
                    BackgroundSeconds = Random.Shared.Next(5, 31),
                    IsFinal = isFinal
                });
            }
        }

        return records;
    }
}
