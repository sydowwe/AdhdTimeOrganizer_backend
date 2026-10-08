using System.Collections.Concurrent;
using System.Security.Claims;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sydowwe.Framework.Contracts.notification;
using Sydowwe.Framework.Contracts.notification.payload;
using Sydowwe.Framework.Contracts.scheduling;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.domain.extServiceContract.user;
using Sydowwe.Framework.Testing;
using Sydowwe.Notifications.application.job;
using Sydowwe.Notifications.domain.entity;
using Sydowwe.Notifications.infrastructure;
using Sydowwe.Notifications.infrastructure.email;
using Sydowwe.Notifications.infrastructure.webPush;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// FO5's back-off: a user away three days or more gets the routine nudges in the bell, not on their phone or in
/// their inbox. Everything else still reaches them.
/// <para>
/// Every assertion is on a stored row or on what the fake senders were handed, because the hold fails silently in
/// both directions: a dropped reader registration pushes nudges to people who are not there, and a hold that is
/// too wide swallows a reminder the user set. Three recipients in every case, so the rule is pinned against both
/// neighbours: one away, one present, one the stamp has never measured (never away, by
/// <see cref="UserPresence.IsAbsent"/>).
/// </para>
/// </summary>
[Collection("Postgres")]
public class NotificationAwayHoldTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long AwayUserId = FakeLoggedUserService.TestUserId;
    private const long PresentUserId = 90_101;
    private const long NeverMeasuredUserId = 90_102;

    private const string PresentEmail = "present@test.com";

    private static readonly KeyValuePair<string, string?>[] PushAndEmailOn =
    [
        new($"{EmailNotificationOptions.SectionName}:Enabled", "true"),
        new($"{PushNotificationOptions.SectionName}:VapidSubject", "mailto:test@example.com"),
        new($"{PushNotificationOptions.SectionName}:VapidPublicKey", "test-public-key"),
        new($"{PushNotificationOptions.SectionName}:VapidPrivateKey", "test-private-key")
    ];

    private static readonly long[] Everyone = [AwayUserId, PresentUserId, NeverMeasuredUserId];

    /// <summary>
    /// The away user was last seen a little over the threshold ago, the present one an hour ago. Every recipient
    /// has a push subscription, and the away and present ones have opted into e-mail for the routine digest (it is
    /// off by default), so a hold on either background channel shows up as a missing send.
    /// </summary>
    protected override async Task SeedAsync(DbContext db)
    {
        var now = DateTime.UtcNow;

        var away = await db.Set<User>().IgnoreQueryFilters().SingleAsync(u => u.Id == AwayUserId);
        away.LastActiveAt = now - UserPresence.QuietAfter - TimeSpan.FromHours(2);

        db.Add(NewUser(PresentUserId, PresentEmail, lastActiveAt: now - TimeSpan.FromHours(1)));
        db.Add(NewUser(NeverMeasuredUserId, "never-measured@test.com", lastActiveAt: null));

        foreach (var userId in Everyone)
            db.Add(new PushSubscription
            {
                UserId = userId,
                Endpoint = EndpointOf(userId),
                P256dh = "test-p256dh",
                Auth = "test-auth"
            });

        foreach (var userId in new[] { AwayUserId, PresentUserId })
            db.Add(new NotificationPreference
            {
                UserId = userId,
                Type = NotificationType.RoutineSuggestedToday,
                Channel = NotificationChannel.Email,
                Enabled = true
            });

        await db.SaveChangesAsync();
    }

    [Fact(DisplayName = "A routine nudge to an away user waits in the bell; the others are pushed as usual")]
    public async Task HeldKind_ReachesTheAwayUsersBellOnly()
    {
        var push = new CapturingWebPushSender();
        var email = new CapturingEmailSender();
        await using var factory = CreateFactory(push, email);

        await using (var scope = factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<INotificationService>().NotifyAsync(
                NotificationRecipients.Users(Everyone),
                new RoutineSuggestedTodayPayload(DateOnly.FromDateTime(DateTime.UtcNow), Count: 1),
                CancellationToken);

        await using var db = CreateDbContext();
        var rows = await db.Set<Notification>().ToDictionaryAsync(n => n.UserId, CancellationToken);

        rows.Keys.Should().BeEquivalentTo(Everyone,
            "the hold withholds the background channels, never the history row: the nudge waits in the bell");
        rows[AwayUserId].DeferredUntil.Should().BeNull(
            "a held nudge is never released later, so the flush must not be handed it");

        push.Endpoints.Should().BeEquivalentTo([EndpointOf(PresentUserId), EndpointOf(NeverMeasuredUserId)],
            "only the away user is held; a user the stamp never measured is not away");
        email.Recipients.Should().BeEquivalentTo([PresentEmail],
            "the away user opted into e-mail for this kind too, and is held on that channel as well");
    }

    [Fact(DisplayName = "A reminder the away user set is still pushed to them")]
    public async Task UnheldKind_ReachesTheAwayUserAsUsual()
    {
        var push = new CapturingWebPushSender();
        await using var factory = CreateFactory(push, new CapturingEmailSender());

        await using (var scope = factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<INotificationService>().NotifyAsync(
                NotificationRecipients.User(AwayUserId),
                new PersonalReminderPayload(ReminderId: 1, Title: "Renew passport"),
                CancellationToken);

        push.Endpoints.Should().BeEquivalentTo([EndpointOf(AwayUserId)],
            "the user asked for this one, so being away holds nothing back");
    }

    /// <summary>
    /// A routine nudge held overnight by quiet hours, whose recipient has gone away since. The window lifting does
    /// not mean anyone is there, so the flush holds it too. Two controls in the same flush prove the rows it did
    /// send render and deliver: the same kind for the present user, and an unheld kind for the away user.
    /// </summary>
    [Fact(DisplayName = "The quiet-hours flush does not release a held kind to a user who has gone away")]
    public async Task DeferredFlush_HoldsAHeldKindForAnAwayUser()
    {
        var push = new CapturingWebPushSender();
        await using var factory = CreateFactory(push, new CapturingEmailSender());

        var flushAt = DateTimeOffset.UtcNow;
        var heldId = await SeedDeferredAsync(AwayUserId, NotificationType.RoutineSuggestedToday, flushAt);
        await SeedDeferredAsync(PresentUserId, NotificationType.RoutineSuggestedToday, flushAt);
        await SeedDeferredAsync(AwayUserId, NotificationType.Test, flushAt);

        await using (var scope = factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetServices<IScheduledJobHandler>()
                .OfType<FlushDeferredNotificationsJobHandler>()
                .Single(h => h.Key == FlushDeferredNotificationsJobHandler.HandlerKey)
                .FlushDeferredAsync(flushAt, CancellationToken);

        push.Endpoints.Should().BeEquivalentTo([EndpointOf(PresentUserId), EndpointOf(AwayUserId)],
            "the present user's nudge and the away user's unheld notification go out; the away user's nudge does not");

        await using var db = CreateDbContext();
        (await db.Set<Notification>().SingleAsync(n => n.Id == heldId, CancellationToken)).DeferredUntil.Should().BeNull(
            "the held row is cleared like any other, or every fifteen-minute sweep would be handed it again");
    }

    // ---- helpers ----------------------------------------------------------------------------------

    private ITestClientFactory CreateFactory(CapturingWebPushSender push, CapturingEmailSender email) =>
        CreateFactory(
            TestRoles.AdminAndUser,
            configureServices: services =>
            {
                services.AddSingleton<IWebPushSender>(push);
                services.AddSingleton<INotificationEmailSender>(email);
                // Every routine nudge comes from a scheduled job, with nobody logged in. The test factory's
                // fake user would otherwise narrow the dispatcher's subscription and preference reads to itself.
                services.AddScoped<ILoggedUserService, BackgroundLoggedUserService>();
            },
            configOverrides: PushAndEmailOn);

    private static string EndpointOf(long userId) => $"https://push.example.test/away-hold-{userId}";

    private static User NewUser(long id, string email, DateTime? lastActiveAt) => new()
    {
        Id = id,
        Email = email,
        NormalizedEmail = email.ToUpperInvariant(),
        UserName = email,
        NormalizedUserName = email.ToUpperInvariant(),
        EmailConfirmed = true,
        SecurityStamp = Guid.NewGuid().ToString("N"),
        ConcurrencyStamp = Guid.NewGuid().ToString("N"),
        Locale = AvailableLocales.En,
        Timezone = TimeZoneInfo.Utc,
        LastActiveAt = lastActiveAt
    };

    /// <summary>A row quiet hours withheld, due now. Written straight to the table, as the quiet-hours tests do.</summary>
    private async Task<long> SeedDeferredAsync(long userId, NotificationType type, DateTimeOffset flushAt)
    {
        await using var db = CreateDbContext();
        var notification = new Notification
        {
            UserId = userId,
            Type = type,
            PayloadJson = type == NotificationType.Test ? """{"message":"held"}""" : """{"count":1}""",
            DeferredUntil = flushAt.AddMinutes(-1)
        };
        db.Add(notification);
        await db.SaveChangesAsync(CancellationToken);
        return notification.Id;
    }

    private sealed class BackgroundLoggedUserService : ILoggedUserService
    {
        public ClaimsPrincipal? LoggedUserPrincipal => null;
        public bool IsAuthenticated => false;
        public IEnumerable<string> GetRoles => [];
        public string GetEmail => throw new InvalidOperationException("No logged user in a background context.");
        public long GetUserId => throw new InvalidOperationException("No logged user in a background context.");
        public long? GetUserIdOrNull => null;
    }

    private sealed class CapturingWebPushSender : IWebPushSender
    {
        public ConcurrentBag<string> Endpoints { get; } = [];

        public Task<bool> SendAsync(PushTarget target, string payloadJson, CancellationToken ct = default)
        {
            Endpoints.Add(target.Endpoint);
            return Task.FromResult(false);
        }
    }

    private sealed class CapturingEmailSender : INotificationEmailSender
    {
        public ConcurrentBag<string> Recipients { get; } = [];

        public Task SendAsync(string email, string subject, string htmlBody, CancellationToken ct = default)
        {
            Recipients.Add(email);
            return Task.CompletedTask;
        }
    }
}
