using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.IntegrationTests.Reminders;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Notifications.Testing.baseTests;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// This host's run of the Notifications module's item-state contract (backend ask B3) — the rules themselves
/// live in <see cref="BaseNotificationItemStateTests"/>, in the module's own testing package, so every host
/// that ships the module inherits them instead of copying them.
/// <para>
/// All this portal owes the base is a second real user to be denied as: <c>Notification.UserId</c> is FK'd to
/// <c>CoreUser</c> by this composition root, so the row has to exist before a foreign notification can.
/// </para>
/// </summary>
[Collection("Postgres")]
public class NotificationItemStateTests(AppDbContextFixture fixture) : BaseNotificationItemStateTests(fixture)
{
    protected override async Task<long> SeedOtherUserAsync(DbContext db)
    {
        await ReminderSeedHelper.EnsureOtherUserAsync(db, CancellationToken);
        return ReminderSeedHelper.OtherUserId;
    }
}
