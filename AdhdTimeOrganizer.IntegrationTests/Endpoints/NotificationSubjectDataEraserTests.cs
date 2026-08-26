using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.IntegrationTests.Reminders;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Notifications.Testing.baseTests;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// This host's run of the Notifications module's reusable GDPR erasure contract. The module base owns the
/// row-level privacy assertions; this composition root only supplies the second concrete <c>CoreUser</c>
/// required by its foreign-recipient guard.
/// </summary>
[Collection("Postgres")]
public class NotificationSubjectDataEraserTests(AppDbContextFixture fixture)
    : BaseNotificationSubjectDataEraserTests(fixture)
{
    protected override async Task<long> SeedOtherUserAsync(DbContext db)
    {
        await ReminderSeedHelper.EnsureOtherUserAsync(db, CancellationToken);
        return ReminderSeedHelper.OtherUserId;
    }
}
