using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sydowwe.Framework.Contracts.notification;
using Sydowwe.Framework.Contracts.notification.payload;
using Sydowwe.Framework.Contracts.scheduling;
using Sydowwe.Framework.Contracts.user;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.Testing;
using Sydowwe.Notifications.application.job;
using Sydowwe.Notifications.domain.entity;
using Sydowwe.Notifications.infrastructure;
using Sydowwe.Notifications.infrastructure.email;
using Sydowwe.Notifications.infrastructure.webPush;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// The two halves of backend ask B1: quiet hours belong to the recipient's own clock, and a delivery that
/// arrives long after the thing it was about says so.
/// <para>
/// Both mechanisms fail <b>silently</b>, which is why every assertion here is on a stored row or on the bytes
/// of the push document, never on a status code:
/// </para>
/// <list type="number">
/// <item>The window used to be evaluated against <c>Application:Timezone</c> while the client rendered the
/// same two ints as bare wall-clock times with no zone at all. The two agreed only because every account sat
/// in that zone — so the bug was invisible in production and invisible in a test suite whose only user is on
/// UTC. <see cref="QuietHours_AreEvaluatedInEachRecipientsOwnZone_NotOneDeploymentZone"/> gives two users the
/// <b>same</b> minutes in different zones at the <b>same</b> instant: under the old reading both outcomes are
/// identical, under the new one exactly one of them defers.</item>
/// <item>The zone now on <c>GET /notification-quiet-hours</c> is what stops the client guessing again. It has
/// to be IANA whichever OS produced the response — <c>TimeZoneInfo.Id</c> is <c>"Central Europe Standard
/// Time"</c> on the Windows box this is developed on and <c>"Europe/Bratislava"</c> in the Linux container it
/// ships in, and only the second is a string a browser accepts.</item>
/// <item>The late marker is absent on the overwhelming majority of notifications, so a broken threshold shows
/// up as either a marker on everything (noise the user learns to ignore) or on nothing (the original
/// complaint). Both directions are asserted, always in pairs.</item>
/// </list>
/// </summary>
[Collection("Postgres")]
public class QuietHoursUserZoneAndLateDeliveryTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long TestUserId = FakeLoggedUserService.TestUserId;

    /// <summary>A second recipient, on UTC, used as the control against <see cref="FarEastZone"/>.</summary>
    private const long UtcUserId = 90_001;

    /// <summary>
    /// UTC+12, fixed. A real IANA id (so the <c>Timezone</c> value converter accepts it) with <b>no DST rule</b>,
    /// so these tests exercise the zone question and nothing else — a zone with a transition would make the
    /// half-day offset below drift by an hour twice a year and turn a deterministic test into a seasonal one.
    /// </summary>
    private static readonly TimeZoneInfo FarEastZone = TimeZoneInfo.FindSystemTimeZoneById("Etc/GMT-12");

    /// <summary>The app's home zone, and the one whose Windows id differs from its IANA id.</summary>
    private static readonly TimeZoneInfo Bratislava = TimeZoneInfo.FindSystemTimeZoneById("Europe/Bratislava");

    /// <summary>
    /// No background channel at all: push unconfigured, and SMTP switched off explicitly — the fixture sets
    /// every MAIL_* var, so the dispatcher would otherwise open a socket to a fake host. Only for cases that do
    /// not need a deferral: with nothing but InApp deliverable, the dispatcher has nothing to withhold and never
    /// stamps <c>DeferredUntil</c>.
    /// </summary>
    private static readonly KeyValuePair<string, string?>[] EmailOff =
        [new($"{EmailNotificationOptions.SectionName}:Enabled", "false")];

    /// <summary>
    /// Push configured, SMTP off. The quiet-hours cases need this: quiet hours only ever defer the background
    /// channels, and the dispatcher discounts a channel the deployment cannot deliver on (framework 240dc58), so
    /// without a configured one no recipient is ever deferred and <c>DeferredUntil</c> stays null whatever the
    /// window says. No subscription is seeded — the assertions are on the row, not on a send.
    /// </summary>
    private static readonly KeyValuePair<string, string?>[] PushOn =
    [
        new($"{EmailNotificationOptions.SectionName}:Enabled", "false"),
        new($"{PushNotificationOptions.SectionName}:VapidSubject", "mailto:test@example.com"),
        new($"{PushNotificationOptions.SectionName}:VapidPublicKey", "test-public-key"),
        new($"{PushNotificationOptions.SectionName}:VapidPrivateKey", "test-private-key")
    ];

    private static readonly KeyValuePair<string, string?>[] PushAndEmailOn =
    [
        new($"{EmailNotificationOptions.SectionName}:Enabled", "true"),
        new($"{PushNotificationOptions.SectionName}:VapidSubject", "mailto:test@example.com"),
        new($"{PushNotificationOptions.SectionName}:VapidPublicKey", "test-public-key"),
        new($"{PushNotificationOptions.SectionName}:VapidPrivateKey", "test-private-key")
    ];

    /// <summary>
    /// Two recipients half a day apart. The baseline fixture seeds the first on UTC; this moves it to UTC+12
    /// and adds a second, real user row still on UTC — real, because the zone is read off the user table and
    /// a missing row would fall back to UTC for the wrong reason and pass the test by accident.
    /// </summary>
    protected override async Task SeedAsync(DbContext db)
    {
        var user = await db.Set<User>().IgnoreQueryFilters().SingleAsync(u => u.Id == TestUserId);
        user.Timezone = FarEastZone;

        db.Add(new User
        {
            Id = UtcUserId,
            Email = "utc-user@test.com",
            NormalizedEmail = "UTC-USER@TEST.COM",
            UserName = "utc-user@test.com",
            NormalizedUserName = "UTC-USER@TEST.COM",
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N"),
            Locale = AvailableLocales.En,
            Timezone = TimeZoneInfo.Utc
        });

        await db.SaveChangesAsync();
    }

    // ---- (1) whose midnight -----------------------------------------------------------------------

    /// <summary>
    /// The core of B1. Both users get the identical window — a two-hour span centred on <b>the UTC+12 user's</b>
    /// current local time — and one notification is raised to both at one instant. Twelve hours away, that same
    /// span cannot also contain the UTC user's local time, so the two must diverge: the UTC+12 user is inside
    /// their quiet hours and has background delivery withheld, the UTC user is not and does not.
    /// <para>
    /// Under the old deployment-zone reading this test is unpassable in either direction — one zone was applied
    /// to both rows, so both deferred or neither did, whichever way the deployment zone happened to fall. That
    /// is exactly the property being pinned: the answer has to depend on <i>whose</i> window it is.
    /// </para>
    /// </summary>
    [Fact(DisplayName = "The same window minutes defer one recipient and not another, because the zone is theirs")]
    public async Task QuietHours_AreEvaluatedInEachRecipientsOwnZone_NotOneDeploymentZone()
    {
        var now = DateTimeOffset.UtcNow;
        var (startMinute, endMinute) = WindowAround(now, FarEastZone, TimeSpan.FromHours(1));

        await using (var seed = CreateDbContext())
        {
            seed.Add(new NotificationQuietHours { UserId = TestUserId, StartMinute = startMinute, EndMinute = endMinute });
            seed.Add(new NotificationQuietHours { UserId = UtcUserId, StartMinute = startMinute, EndMinute = endMinute });
            await seed.SaveChangesAsync(CancellationToken);
        }

        await using var factory = CreateFactory(TestRoles.AdminAndUser, configOverrides: PushOn);
        await using (var scope = factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<INotificationService>().NotifyAsync(
                NotificationRecipients.Users([TestUserId, UtcUserId]),
                new TestNotificationPayload("zone check"),
                CancellationToken);

        await using var db = CreateDbContext();
        var byUser = await db.Set<Notification>().ToDictionaryAsync(n => n.UserId, CancellationToken);

        byUser[TestUserId].DeferredUntil.Should().NotBeNull(
            "it is the middle of this recipient's own quiet window on their own clock");
        byUser[UtcUserId].DeferredUntil.Should().BeNull(
            "the identical minutes land twelve hours away on this recipient's clock, nowhere near their window — "
            + "if this also deferred, one zone is being applied to both windows again");
    }

    /// <summary>
    /// The resume instant is the window's end <i>in the owner's zone</i>, not in UTC. Pinned separately from
    /// the defer/don't-defer decision above because they are two different reads of the same zone: getting the
    /// first right and the second wrong would withhold the push correctly and then release it up to twelve
    /// hours off, which no assertion on <c>DeferredUntil != null</c> would catch.
    /// </summary>
    [Fact(DisplayName = "A deferred notification resumes when the window ends on the recipient's clock")]
    public async Task DeferredUntil_IsTheWindowEndInTheRecipientsOwnZone()
    {
        var now = DateTimeOffset.UtcNow;
        var (startMinute, endMinute) = WindowAround(now, FarEastZone, TimeSpan.FromHours(1));

        await using (var seed = CreateDbContext())
        {
            seed.Add(new NotificationQuietHours { UserId = TestUserId, StartMinute = startMinute, EndMinute = endMinute });
            await seed.SaveChangesAsync(CancellationToken);
        }

        await using var factory = CreateFactory(TestRoles.AdminAndUser, configOverrides: PushOn);
        await using (var scope = factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<INotificationService>().NotifyAsync(
                NotificationRecipients.User(TestUserId), new TestNotificationPayload("resume check"), CancellationToken);

        await using var db = CreateDbContext();
        var notification = await db.Set<Notification>().SingleAsync(CancellationToken);

        // The window was centred on `now`, so its far edge is an hour out — in the user's zone. Read in any
        // other zone the same minutes resolve to a different day boundary and the answer moves by hours.
        notification.DeferredUntil.Should().NotBeNull();
        notification.DeferredUntil!.Value.Should().BeCloseTo(now.AddHours(1), TimeSpan.FromMinutes(2));
    }

    /// <summary>
    /// The seam the whole change hangs on. It is generic over the host's user type, so no marker scan can
    /// register it — a missing line in <c>ModuleServiceExtensions</c> means every window silently falls back to
    /// UTC, which throws nothing and logs nothing. This asserts on the resolved <i>value</i> rather than the
    /// registration (<c>ModuleWiringTests</c> covers that) so a reader that resolves but reads the wrong column
    /// also fails.
    /// </summary>
    [Fact(DisplayName = "The user time-zone seam returns each user's own stored zone")]
    public async Task UserTimeZoneReader_ReturnsTheStoredZonePerUser()
    {
        await using var factory = CreateFactory(TestRoles.AdminAndUser, configOverrides: EmailOff);
        await using var scope = factory.Services.CreateAsyncScope();

        var zones = await scope.ServiceProvider.GetRequiredService<IUserTimeZoneReader>()
            .GetTimeZonesAsync([TestUserId, UtcUserId], CancellationToken);

        zones[TestUserId].Should().Be(FarEastZone);
        zones[UtcUserId].Should().Be(TimeZoneInfo.Utc);
    }

    // ---- the contract the client renders ----------------------------------------------------------

    /// <summary>
    /// <c>GET /notification-quiet-hours</c> names the clock its two ints belong to, in IANA form. The
    /// normalization is the point: on the Windows machine this is developed on <c>TimeZoneInfo.Id</c> for this
    /// zone is <c>"Central Europe Standard Time"</c>, which no browser API accepts — so an un-normalized
    /// response passes every server-side check and breaks only in the client, on one OS.
    /// </summary>
    [Fact(DisplayName = "The quiet-hours read names the caller's zone as an IANA id")]
    public async Task GetQuietHours_ReturnsTheCallersZoneAsIana()
    {
        await using (var seed = CreateDbContext())
        {
            var user = await seed.Set<User>().IgnoreQueryFilters().SingleAsync(u => u.Id == TestUserId, CancellationToken);
            user.Timezone = Bratislava;
            seed.Add(new NotificationQuietHours { UserId = TestUserId, StartMinute = 22 * 60, EndMinute = 6 * 60 });
            await seed.SaveChangesAsync(CancellationToken);
        }

        var response = await CreateClient().GetAsync("api/notification-quiet-hours", CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts, CancellationToken);
        var window = body.GetProperty("quietHours");

        window.GetProperty("startMinute").GetInt32().Should().Be(22 * 60);
        window.GetProperty("endMinute").GetInt32().Should().Be(6 * 60);
        window.GetProperty("timeZone").GetString().Should().Be("Europe/Bratislava",
            "the client hands this straight to Intl — a Windows zone id here breaks the settings card on a "
            + "Windows-built server and nowhere else");
    }

    /// <summary>
    /// No window ⇒ no zone either. The zone describes how to read <i>these</i> minutes, so returning one beside
    /// a null window would invite a client to render a window nobody set.
    /// </summary>
    [Fact(DisplayName = "No quiet-hours row returns a null window rather than a zone with no window")]
    public async Task GetQuietHours_ReturnsNullWhenNoWindowIsSet()
    {
        var response = await CreateClient().GetAsync("api/notification-quiet-hours", CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts, CancellationToken);
        body.GetProperty("quietHours").ValueKind.Should().Be(JsonValueKind.Null);
    }

    // ---- (3) marked-late delivery -----------------------------------------------------------------

    /// <summary>
    /// A producer that knows what its notification was about passes that instant; the module stores it only if
    /// this delivery is materially behind it. Both directions in one test, because the failure mode of a
    /// threshold is asymmetric and each half looks fine alone: stamping unconditionally makes every reminder
    /// render "was due at …", stamping never leaves the original complaint exactly as it was.
    /// </summary>
    [Fact(DisplayName = "An original-due instant is stored when the delivery is late and dropped when it is not")]
    public async Task OriginallyDueAt_IsStoredOnlyForAMateriallyLateDelivery()
    {
        var now = DateTimeOffset.UtcNow;
        var longAgo = now - LateDeliveryPolicy.Threshold - TimeSpan.FromMinutes(5);
        var justNow = now - TimeSpan.FromMinutes(1);

        await using var factory = CreateFactory(TestRoles.AdminAndUser, configOverrides: EmailOff);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();

            await notifications.NotifyAsync(
                NotificationRecipients.User(TestUserId), NotificationType.Test,
                new TestNotificationPayload("stale"), longAgo, ct: CancellationToken);

            await notifications.NotifyAsync(
                NotificationRecipients.User(UtcUserId), NotificationType.Test,
                new TestNotificationPayload("punctual"), justNow, ct: CancellationToken);
        }

        await using var db = CreateDbContext();
        var byUser = await db.Set<Notification>().ToDictionaryAsync(n => n.UserId, CancellationToken);

        byUser[TestUserId].OriginallyDueAt.Should().NotBeNull();
        byUser[TestUserId].OriginallyDueAt!.Value.Should().BeCloseTo(longAgo, TimeSpan.FromSeconds(5));

        byUser[UtcUserId].OriginallyDueAt.Should().BeNull(
            "a minute behind is not late — a marker here would put 'was due at …' on essentially every reminder");
    }

    /// <summary>
    /// The other half of B1/3b: the notification itself was punctual, but quiet hours held its background
    /// channels overnight. The flush derives the due instant from <c>CreatedTimestamp</c> and writes it into
    /// the push document's <c>data</c>, so the client can say what the notification was about instead of
    /// showing a live nudge for something that is over.
    /// <para>
    /// Asserted on the JSON that actually goes over the wire, not on a row: the key lives in the nested
    /// <c>data</c> object precisely so the service worker keeps rendering <c>title</c>/<c>body</c> verbatim,
    /// and a well-meaning move to the top level would be invisible to any row-level check.
    /// </para>
    /// </summary>
    [Fact(DisplayName = "A flush released long after the send marks the push as late; a prompt one does not")]
    public async Task DeferredFlush_MarksALateDeliveryInThePushPayload()
    {
        var sender = new CapturingWebPushSender();

        await using var factory = CreateFactory(
            TestRoles.AdminAndUser,
            configureServices: services => services.AddSingleton<IWebPushSender>(sender),
            configOverrides: PushOn);

        var flushAt = DateTimeOffset.UtcNow;
        var heldOvernight = flushAt - LateDeliveryPolicy.Threshold - TimeSpan.FromHours(6);
        var heldBriefly = flushAt - TimeSpan.FromMinutes(2);

        await SeedPushSubscriptionAsync(TestUserId);
        var lateId = await SeedDeferredNotificationAsync(TestUserId, createdAt: heldOvernight, deferredUntil: flushAt.AddMinutes(-1));

        await using (var scope = factory.Services.CreateAsyncScope())
            await ResolveFlushHandler(scope.ServiceProvider).FlushDeferredAsync(flushAt, CancellationToken);

        var latePayload = sender.Payloads.Should().ContainSingle().Subject;
        var lateData = JsonDocument.Parse(latePayload).RootElement.GetProperty("data");
        lateData.GetProperty("id").GetInt64().Should().Be(lateId);
        lateData.TryGetProperty("originallyDueAt", out var dueAt).Should().BeTrue(
            "this push is arriving six hours after the notification was raised");
        dueAt.GetDateTimeOffset().Should().BeCloseTo(heldOvernight, TimeSpan.FromSeconds(5));

        // Control: same mechanism, a window that lifted two minutes later. Omitted entirely rather than
        // written as null, matching how tag and url are handled in the same document.
        sender.Payloads.Clear();
        await SeedDeferredNotificationAsync(TestUserId, createdAt: heldBriefly, deferredUntil: flushAt.AddMinutes(-1));

        await using (var scope = factory.Services.CreateAsyncScope())
            await ResolveFlushHandler(scope.ServiceProvider).FlushDeferredAsync(flushAt, CancellationToken);

        var promptPayload = sender.Payloads.Should().ContainSingle().Subject;
        JsonDocument.Parse(promptPayload).RootElement.GetProperty("data")
            .TryGetProperty("originallyDueAt", out _).Should().BeFalse(
                "a two-minute hold is not late, and a marker on it is noise that trains the user to ignore the real ones");
    }

    [Fact(DisplayName = "An overnight quiet-hours deferral crosses spring-forward and delivers both background channels once")]
    public async Task QuietHoursDeferral_CrossesSpringForward_AndFlushesWebPushAndEmail()
    {
        var sentAt = new DateTimeOffset(2026, 3, 28, 22, 30, 0, TimeSpan.Zero);
        var push = new CapturingWebPushSender();
        var email = new CapturingEmailSender();

        await using (var seed = CreateDbContext())
        {
            var user = await seed.Set<User>().IgnoreQueryFilters().SingleAsync(u => u.Id == TestUserId, CancellationToken);
            user.Timezone = Bratislava;
            seed.AddRange(
                new NotificationQuietHours { UserId = TestUserId, StartMinute = 22 * 60, EndMinute = 6 * 60 },
                new NotificationPreference { UserId = TestUserId, Type = NotificationType.Test, Channel = NotificationChannel.Email, Enabled = true },
                new PushSubscription
                {
                    UserId = TestUserId,
                    Endpoint = $"https://push.example.test/dst-{TestUserId}",
                    P256dh = "test-p256dh",
                    Auth = "test-auth"
                });
            await seed.SaveChangesAsync(CancellationToken);
        }

        await using var factory = CreateFactory(
            TestRoles.AdminAndUser,
            configureServices: services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(sentAt));
                services.AddSingleton<IWebPushSender>(push);
                services.AddSingleton<INotificationEmailSender>(email);
            },
            configOverrides: PushAndEmailOn);

        await using (var scope = factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<INotificationService>().NotifyAsync(
                NotificationRecipients.User(TestUserId), new TestNotificationPayload("spring-forward"), CancellationToken);

        Notification deferred;
        await using (var db = CreateDbContext())
        {
            deferred = await db.Set<Notification>().SingleAsync(CancellationToken);
            deferred.DeferredUntil.Should().Be(new DateTimeOffset(2026, 3, 29, 4, 0, 0, TimeSpan.Zero),
                "06:00 in Bratislava follows the skipped 02:00 hour, so its UTC instant is 04:00 rather than 05:00");
        }
        push.Payloads.Should().BeEmpty("quiet hours withhold Web Push until the wall-clock window ends");
        email.Messages.Should().BeEmpty("quiet hours withhold email until the wall-clock window ends");

        await using (var scope = factory.Services.CreateAsyncScope())
            await ResolveFlushHandler(scope.ServiceProvider).FlushDeferredAsync(deferred.DeferredUntil!.Value, CancellationToken);

        push.Payloads.Should().ContainSingle();
        email.Messages.Should().ContainSingle().Which.Email.Should().Be(FakeLoggedUserService.TestUserEmail);

        await using var verify = CreateDbContext();
        (await verify.Set<Notification>().SingleAsync(n => n.Id == deferred.Id, CancellationToken)).DeferredUntil.Should().BeNull(
            "the delivery was released, so the next fifteen-minute sweep must not send it a second time");
    }

    // ---- helpers ----------------------------------------------------------------------------------

    /// <summary>
    /// A quiet window centred on <paramref name="instant"/> as read in <paramref name="zone"/>, half-width
    /// <paramref name="halfWidth"/>. Derived from the real clock rather than hard-coded because
    /// <c>NotifyAsync</c> takes its own <c>UtcNow</c> and cannot be driven to a fixed instant — so the window
    /// moves to meet the clock instead. Wraps past midnight cleanly, which is the overnight case anyway.
    /// </summary>
    private static (int StartMinute, int EndMinute) WindowAround(DateTimeOffset instant, TimeZoneInfo zone, TimeSpan halfWidth)
    {
        var local = TimeZoneInfo.ConvertTime(instant, zone);
        var minuteOfDay = local.Hour * 60 + local.Minute;
        var half = (int)halfWidth.TotalMinutes;

        var start = ((minuteOfDay - half) % QuietHoursPolicy.MinutesPerDay + QuietHoursPolicy.MinutesPerDay) % QuietHoursPolicy.MinutesPerDay;
        var end = (minuteOfDay + half) % QuietHoursPolicy.MinutesPerDay;
        return (start, end);
    }

    /// <summary>
    /// The flush job out of <c>IEnumerable&lt;IScheduledJobHandler&gt;</c> by its string key — the same lookup
    /// the Scheduler dispatcher does at fire time, and the only one that proves the handler is reachable the
    /// way production reaches it. It is registered by interface (the <c>IScopedService</c> marker scan binds
    /// <c>AsImplementedInterfaces</c>), so asking for the concrete type resolves nothing.
    /// </summary>
    private static FlushDeferredNotificationsJobHandler ResolveFlushHandler(IServiceProvider services) =>
        services.GetServices<IScheduledJobHandler>()
            .OfType<FlushDeferredNotificationsJobHandler>()
            .Single(h => h.Key == FlushDeferredNotificationsJobHandler.HandlerKey);

    private async Task SeedPushSubscriptionAsync(long userId)
    {
        await using var db = CreateDbContext();
        db.Add(new PushSubscription
        {
            UserId = userId,
            Endpoint = $"https://push.example.test/{userId}",
            P256dh = "test-p256dh",
            Auth = "test-auth"
        });
        await db.SaveChangesAsync(CancellationToken);
    }

    /// <summary>
    /// A notification already withheld by quiet hours, written straight to the table. Inserted rather than
    /// produced through <c>NotifyAsync</c> because the point is a flush happening hours after the send, and the
    /// send instant is the one thing <c>NotifyAsync</c> will not let a caller choose.
    /// <para>
    /// <c>CreatedTimestamp</c> is forced with <c>ExecuteUpdate</c> <b>after</b> the insert, not assigned on the
    /// entity: the timestamp interceptor stamps it to <c>UtcNow</c> on add and silently discards anything set
    /// by hand, which would leave every seeded row "just now" and make the late assertion below untestable —
    /// it would fail looking exactly like a broken threshold.
    /// </para>
    /// </summary>
    private async Task<long> SeedDeferredNotificationAsync(long userId, DateTimeOffset createdAt, DateTimeOffset deferredUntil)
    {
        await using var db = CreateDbContext();
        var notification = new Notification
        {
            UserId = userId,
            Type = NotificationType.Test,
            PayloadJson = """{"message":"held"}""",
            DeferredUntil = deferredUntil
        };
        db.Add(notification);
        await db.SaveChangesAsync(CancellationToken);

        await db.Set<Notification>()
            .Where(n => n.Id == notification.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.CreatedTimestamp, createdAt.UtcDateTime), CancellationToken);

        return notification.Id;
    }

    private sealed class CapturingWebPushSender : IWebPushSender
    {
        public ConcurrentBag<string> Payloads { get; } = [];

        public Task<bool> SendAsync(PushTarget target, string payloadJson, CancellationToken ct = default)
        {
            Payloads.Add(payloadJson);
            return Task.FromResult(false);
        }
    }

    private sealed class CapturingEmailSender : INotificationEmailSender
    {
        public ConcurrentBag<(string Email, string Subject, string HtmlBody)> Messages { get; } = [];

        public Task SendAsync(string email, string subject, string htmlBody, CancellationToken ct = default)
        {
            Messages.Add((email, subject, htmlBody));
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
