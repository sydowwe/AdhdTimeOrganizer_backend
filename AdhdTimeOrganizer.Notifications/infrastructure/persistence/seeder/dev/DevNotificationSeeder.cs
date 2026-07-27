using AdhdTimeOrganizer.Notifications.domain.entity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MojaDigitalnaFirma.Kernel.notification;
using MojaDigitalnaFirma.Kernel.user;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.infrastructure.persistence.seeder;
using Sydowwe.Framework.infrastructure.persistence.seeder.@interface;

namespace AdhdTimeOrganizer.Notifications.infrastructure.persistence.seeder.dev;

public class DevNotificationSeeder(DbContext dbContext, ISeedUserIdProvider seedUsers, ILogger<DevNotificationSeeder> logger)
    : IAppWideDevSeeder, IScopedService
{
    public string SeederName => "Dev Notifications";
    public int Order => 70; // After employees (1) and users (5)

    public async Task TruncateTable()
    {
        await dbContext.TruncateTableCascadeAsync<Notification>();
    }

    public async Task Seed()
    {
        if (await dbContext.Set<Notification>().AnyAsync())
        {
            logger.LogInformation("Notifications already exist, skipping.");
            return;
        }

        var userIds = await seedUsers.GetSeedUserIdsAsync(3);

        if (userIds.Count == 0)
        {
            logger.LogWarning("No users found. Skipping notification seeding.");
            return;
        }

        var primaryId = userIds[0];
        var secondaryId = userIds.Count > 1 ? userIds[1] : primaryId;

        // Listed newest-first per user: unread entries come before read ones so that after the
        // timestamp staggering below they sort to the top of the bell (ordered by CreatedTimestamp DESC).
        var notifications = new List<Notification>
        {
            // --- Unread for the primary user ---
            new()
            {
                UserId = primaryId,
                Type = NotificationType.DeadlineApproaching,
                PayloadJson = """{"title":"NaplÃ¡novanÃ¡ aktivita â€“ blÃ­Å¾i sa zaÄiatok"}""",
                IsRead = false
            },
            new()
            {
                UserId = primaryId,
                Type = NotificationType.ReminderDigest,
                PayloadJson = """{"count":3}""",
                IsRead = false
            },
            new()
            {
                UserId = primaryId,
                Type = NotificationType.ScheduledJobOverdue,
                PayloadJson = """{"jobKey":"reminder-scan","ownerModule":"Reminders"}""",
                IsRead = false
            },

            // --- Read notifications for the primary user ---
            new()
            {
                UserId = primaryId,
                Type = NotificationType.ScheduledJobFailed,
                PayloadJson = """{"jobKey":"purge-expired-run-logs","ownerModule":"Scheduler","errorType":"TimeoutException"}""",
                IsRead = true
            },
            new()
            {
                UserId = primaryId,
                Type = NotificationType.Test,
                PayloadJson = """{"message":"Testovacia notifikÃ¡cia pre administrÃ¡tora."}""",
                IsRead = true
            },

            // --- Notifications for the secondary user ---
            new()
            {
                UserId = secondaryId,
                Type = NotificationType.ReminderDigest,
                PayloadJson = """{"count":1}""",
                IsRead = false
            },
            new()
            {
                UserId = secondaryId,
                Type = NotificationType.Test,
                PayloadJson = """{"message":"Testovacia notifikÃ¡cia."}""",
                IsRead = true
            }
        };

        dbContext.AddRange(notifications);
        await dbContext.SaveChangesAsync();

        // CreatedTimestamp is auto-stamped to UtcNow on insert (BaseSaveChangesAsync), so every row
        // would otherwise share one instant and the bell (ordered by CreatedTimestamp DESC) would have
        // no meaningful order. Stagger it in a second pass â€” on Modified state only ModifiedTimestamp is
        // re-stamped, so these CreatedTimestamp values survive. ReadAt is derived to always follow creation.
        var now = DateTime.UtcNow;
        for (var i = 0; i < notifications.Count; i++)
        {
            var notification = notifications[i];
            notification.CreatedTimestamp = now.AddHours(-(i + 1) * 6);
            if (notification.IsRead)
                notification.ReadAt = notification.CreatedTimestamp.AddHours(1);
        }

        await dbContext.SaveChangesAsync();

        logger.LogInformation("Seeded {Count} dev notifications.", notifications.Count);
    }
}