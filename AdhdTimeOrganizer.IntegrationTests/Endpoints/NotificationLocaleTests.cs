using AdhdTimeOrganizer.Routines.application.notification;
using AdhdTimeOrganizer.History.application.notification;
using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Json;
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
using Sydowwe.Framework.domain.helper;
using Sydowwe.Framework.Testing;
using Sydowwe.Notifications.application;
using Sydowwe.Notifications.application.job;
using Sydowwe.Notifications.application.text;
using Sydowwe.Notifications.domain.entity;
using Sydowwe.Notifications.infrastructure;
using Sydowwe.Notifications.infrastructure.email;
using Sydowwe.Notifications.infrastructure.webPush;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// Backend ask B15: a notification reads in the recipient's <b>account locale</b>, on every channel.
/// <para>
/// The row stores type + payload and the words are chosen at render time, so there are three render points
/// — the bell read, the live dispatch, the deferred flush — and each is asserted on the bytes it emits,
/// because every way this can regress is silent: a render point that stops passing the locale (or passes the
/// wrong user's) still returns 200, still delivers, and merely goes back to Slovak for everyone, which is the
/// state the ask was filed against.
/// </para>
/// <para>
/// The baseline fixture account is <c>EN</c>; a second, real user row is seeded as <c>SK</c> — real, because
/// the locale is read off the user table and a missing row would fall back to Slovak for the wrong reason and
/// pass half of these by accident.
/// </para>
/// </summary>
[Collection("Postgres")]
public class NotificationLocaleTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long EnglishUserId = FakeLoggedUserService.TestUserId;
    private const long SlovakUserId = 90_015;
    private const string SlovakUserEmail = "sk-user@test.com";

    private static readonly KeyValuePair<string, string?>[] PushAndEmailOn =
    [
        new($"{EmailNotificationOptions.SectionName}:Enabled", "true"),
        new($"{PushNotificationOptions.SectionName}:VapidSubject", "mailto:test@example.com"),
        new($"{PushNotificationOptions.SectionName}:VapidPublicKey", "test-public-key"),
        new($"{PushNotificationOptions.SectionName}:VapidPrivateKey", "test-private-key")
    ];

    /// <summary>SMTP off explicitly (the fixture sets every MAIL_* var, so the dispatcher would open a socket), push on.</summary>
    private static readonly KeyValuePair<string, string?>[] PushOn =
    [
        new($"{EmailNotificationOptions.SectionName}:Enabled", "false"),
        new($"{PushNotificationOptions.SectionName}:VapidSubject", "mailto:test@example.com"),
        new($"{PushNotificationOptions.SectionName}:VapidPublicKey", "test-public-key"),
        new($"{PushNotificationOptions.SectionName}:VapidPrivateKey", "test-private-key")
    ];

    protected override async Task SeedAsync(DbContext db)
    {
        db.Add(new User
        {
            Id = SlovakUserId,
            Email = SlovakUserEmail,
            NormalizedEmail = SlovakUserEmail.ToUpperInvariant(),
            UserName = SlovakUserEmail,
            NormalizedUserName = SlovakUserEmail.ToUpperInvariant(),
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N"),
            Locale = AvailableLocales.Sk,
            Timezone = TimeZoneInfo.Utc
        });

        foreach (var userId in new[] { EnglishUserId, SlovakUserId })
            db.Add(new PushSubscription
            {
                UserId = userId,
                Endpoint = $"https://push.example.test/{userId}",
                P256dh = "test-p256dh",
                Auth = "test-auth"
            });

        await db.SaveChangesAsync();
    }

    // ---- (1) the bell -----------------------------------------------------------------------------

    /// <summary>
    /// The observed defect, and its second half: the bell reads in the caller's locale <i>as it is now</i>.
    /// Nothing is frozen at dispatch — the row holds no text — so switching language re-reads the whole list,
    /// old rows included. The ask offered that as the more expensive option (B); it comes free of the storage.
    /// </summary>
    [Fact(DisplayName = "The bell reads in the account's locale, and follows it when it changes")]
    public async Task BellList_ReadsInTheAccountsLocale_AndFollowsAChange()
    {
        var id = await SeedNotificationAsync(EnglishUserId, NotificationType.ReminderDigest, new ReminderDigestPayload(3));

        var english = await ReadBellListAsync();
        english[id].GetProperty("title").GetString().Should().Be("Reminders");
        english[id].GetProperty("body").GetString().Should().Be("You have 3 scheduled reminders.");

        await using (var db = CreateDbContext())
        {
            var user = await db.Set<User>().IgnoreQueryFilters().SingleAsync(u => u.Id == EnglishUserId, CancellationToken);
            user.Locale = AvailableLocales.Sk;
            await db.SaveChangesAsync(CancellationToken);
        }

        var slovak = await ReadBellListAsync();
        slovak[id].GetProperty("title").GetString().Should().Be("Pripomienky",
            "the same row re-reads in the new language — the text is composed on read, never stored");
        slovak[id].GetProperty("body").GetString().Should().Be("Máte 3 naplánované pripomienky.");
    }

    // ---- (2) the live dispatch --------------------------------------------------------------------

    /// <summary>
    /// One event, two recipients, two languages — and the push document and the mail for each carry that
    /// recipient's words, chrome included. Rule 3 of the ask: the channels are the same text, so localizing
    /// the bell alone would leave the phone and the inbox in Slovak.
    /// </summary>
    [Fact(DisplayName = "A dispatch renders each recipient in their own locale, on push and e-mail alike")]
    public async Task Dispatch_RendersEachRecipientInTheirOwnLocale()
    {
        var push = new CapturingWebPushSender();
        var email = new CapturingEmailSender();

        await using var factory = CreateFactory(
            TestRoles.AdminAndUser,
            configureServices: services =>
            {
                services.AddSingleton<IWebPushSender>(push);
                services.AddSingleton<INotificationEmailSender>(email);
                services.AddScoped<ILoggedUserService, BackgroundLoggedUserService>();
            },
            configOverrides: PushAndEmailOn);

        await using (var scope = factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<INotificationService>().NotifyAsync(
                NotificationRecipients.Users([EnglishUserId, SlovakUserId]),
                NotificationType.DeadlineApproaching,
                new DeadlineApproachingPayload("Feature Development"),
                ct: CancellationToken);

        push.Documents.Should().HaveCount(2);
        PushTitle(push, EnglishUserId).Should().Be("Deadline approaching");
        PushTitle(push, SlovakUserId).Should().Be("Blíži sa termín");

        email.Messages.Should().HaveCount(2);
        var englishMail = email.Messages.Single(m => m.Email == FakeLoggedUserService.TestUserEmail);
        var slovakMail = email.Messages.Single(m => m.Email == SlovakUserEmail);

        englishMail.Subject.Should().Be("Deadline approaching");
        englishMail.HtmlBody.Should().Contain("Deadline approaching: Feature Development.")
            .And.Contain("Open in the portal", "the button is chrome, and chrome is text too")
            .And.NotContain("Otvoriť", "a Slovak footer under an English body is the half-fix the ask rules out");

        slovakMail.Subject.Should().Be("Blíži sa termín");
        slovakMail.HtmlBody.Should().Contain("Blíži sa termín: Feature Development.")
            .And.Contain("Otvoriť v portáli");
    }

    // ---- (3) the deferred flush -------------------------------------------------------------------

    /// <summary>
    /// The third render point is a different code path from the live one — it renders a whole batch of rows
    /// from many recipients at once, keyed to share work — so it is asserted on its own: two withheld rows for
    /// two users release in two languages.
    /// </summary>
    [Fact(DisplayName = "A quiet-hours flush renders each withheld row in its recipient's locale")]
    public async Task DeferredFlush_RendersEachRecipientInTheirOwnLocale()
    {
        var push = new CapturingWebPushSender();

        await using var factory = CreateFactory(
            TestRoles.AdminAndUser,
            configureServices: services =>
            {
                services.AddSingleton<IWebPushSender>(push);
                services.AddScoped<ILoggedUserService, BackgroundLoggedUserService>();
            },
            configOverrides: PushOn);

        var payload = new RoutinePeriodEndedPayload(77, "Weekly", CompletedCount: 4, TotalCount: 5, Streak: 3, Outcome: RoutineStreakOutcome.Broken);
        var released = DateTimeOffset.UtcNow.AddMinutes(-1);
        await SeedNotificationAsync(EnglishUserId, RoutinesNotificationTypes.RoutinePeriodEnded, payload, deferredUntil: released);
        await SeedNotificationAsync(SlovakUserId, RoutinesNotificationTypes.RoutinePeriodEnded, payload, deferredUntil: released);

        await using (var scope = factory.Services.CreateAsyncScope())
            await ResolveFlushHandler(scope.ServiceProvider).FlushDeferredAsync(DateTimeOffset.UtcNow, CancellationToken);

        push.Documents.Should().HaveCount(2);
        PushBody(push, EnglishUserId).Should().Be("“Weekly” has ended: 4 of 5 done. The streak was broken.");
        PushBody(push, SlovakUserId).Should().Be("„Weekly“ sa skončilo: hotové 4 z 5. Séria bola prerušená.",
            "the same payload, rendered once per language present in the flush — not once for the batch");
    }

    // ---- (4) the third locale ---------------------------------------------------------------------

    /// <summary>
    /// Czech reads Slovak, deliberately: the product has no Czech vocabulary anywhere (the client ships SK and
    /// EN), and Slovak is the nearest language. It has to hold for a <b>portal</b> kind through the composed
    /// options, not just for a framework kind through the module's static fallback — the module's own
    /// <c>Cz → Sk</c> arm lands on the module's Slovak table, which does not word this portal's kinds, so the
    /// host maps <c>Cz</c> to its own table explicitly and this is what fails if that line goes.
    /// (Whether every catalogued kind is worded at all, in every locale, is <c>NotificationVocabularyTests</c>.)
    /// </summary>
    [Fact(DisplayName = "A Czech account reads the portal's Slovak until there is a Czech vocabulary")]
    public void CzechLocale_ReadsThePortalsSlovak()
    {
        var renderer = Fixture.AdminAndUserFactory.Services.GetRequiredService<INotificationTextRenderer>();

        var czech = renderer.Render(HistoryNotificationTypes.TimerBoundary, "{}", AvailableLocales.Cz);

        czech.Should().Be(renderer.Render(HistoryNotificationTypes.TimerBoundary, "{}", AvailableLocales.Sk));
        czech.Title.Should().Be("Časovač skončil", "a portal kind, so the portal's table — not the module's generic fallback");
    }

    // ---- (5) the host's say ------------------------------------------------------------------------

    /// <summary>
    /// A host owns the wording every kind shares: subclass the shipped table, override the members it wants, hand
    /// it to <c>NotificationTextOptions.Use</c>. (A kind's own sentences are its owner's, on the kind — R1.) The
    /// override has to reach <b>every</b> render point through the one options object — a renderer or a mail builder
    /// that reached for the static module table instead would keep the shipped wording for that channel only, which
    /// is the kind of drift that shows up in nobody's test but the user's inbox. Asserted on the bell and the mail,
    /// the two ends of the pipeline: the quote style inside a kind's own sentence, and the mail chrome.
    /// </summary>
    [Fact(DisplayName = "A host can reword a language, and the override reaches the bell and the mail alike")]
    public async Task HostOverride_ReachesEveryRenderPoint()
    {
        var email = new CapturingEmailSender();

        await using var factory = CreateFactory(
            TestRoles.AdminAndUser,
            configureServices: services =>
            {
                services.AddSingleton<INotificationEmailSender>(email);
                // Push is configured and this user has a device on file; keep the real sender off the network.
                services.AddSingleton<IWebPushSender>(new CapturingWebPushSender());
                services.AddScoped<ILoggedUserService, BackgroundLoggedUserService>();
                services.Configure<NotificationTextOptions>(o => o.Use(AvailableLocales.En, new HouseStyleEnglish()));
            },
            configOverrides: PushAndEmailOn);

        await using (var scope = factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<INotificationService>().NotifyAsync(
                NotificationRecipients.User(EnglishUserId),
                new ScheduledJobFailedPayload("purge-expired-run-logs"),
                CancellationToken);

        var mail = email.Messages.Should().ContainSingle().Subject;
        mail.Subject.Should().Be("Scheduled job failed", "a member left alone keeps the shipped sentence");
        mail.HtmlBody.Should().Contain("Scheduled job [purge-expired-run-logs] failed", "the kind words itself with the host's quotes")
            .And.Contain("Open the app", "the chrome is built from the same overridden table, not the module's");

        var bell = await ReadBellListAsync(factory.CreateClient());
        bell.Values.Should().ContainSingle().Which.GetProperty("body").GetString()
            .Should().Be("Scheduled job [purge-expired-run-logs] failed and needs attention.");
    }

    /// <summary>Two members reworded, the rest inherited — the shape of a real host override.</summary>
    private sealed class HouseStyleEnglish : EnNotificationTexts
    {
        public override string Quote(string value) => $"[{value}]";
        public override string EmailOpenPortal => "Open the app";
    }

    // ---- helpers ----------------------------------------------------------------------------------

    private async Task<Dictionary<long, JsonElement>> ReadBellListAsync(HttpClient? client = null)
    {
        var response = await (client ?? CreateClient()).GetAsync("api/notification/mine", CancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(CancellationToken);
        return JsonDocument.Parse(json).RootElement.EnumerateArray()
            .ToDictionary(e => e.GetProperty("id").GetInt64(), e => e.Clone());
    }

    private static string? PushTitle(CapturingWebPushSender push, long userId) =>
        push.DocumentFor(userId).GetProperty("title").GetString();

    private static string? PushBody(CapturingWebPushSender push, long userId) =>
        push.DocumentFor(userId).GetProperty("body").GetString();

    private async Task<long> SeedNotificationAsync(long userId, NotificationType type, INotificationPayload payload, DateTimeOffset? deferredUntil = null)
    {
        await using var db = CreateDbContext();
        var notification = new Notification
        {
            UserId = userId,
            Type = type,
            PayloadJson = JsonHelper.Serialize<object>(payload),
            DeferredUntil = deferredUntil
        };
        db.Add(notification);
        await db.SaveChangesAsync(CancellationToken);
        return notification.Id;
    }

    private static FlushDeferredNotificationsJobHandler ResolveFlushHandler(IServiceProvider services) =>
        services.GetServices<IScheduledJobHandler>()
            .OfType<FlushDeferredNotificationsJobHandler>()
            .Single(h => h.Key == FlushDeferredNotificationsJobHandler.HandlerKey);

    /// <summary>
    /// Keeps the document per <i>target</i>: the subscription endpoint is seeded as <c>…/{userId}</c>, so the
    /// recipient of each push is recoverable from where it was sent — which is the whole question here.
    /// </summary>
    private sealed class CapturingWebPushSender : IWebPushSender
    {
        public ConcurrentDictionary<string, string> Documents { get; } = new();

        public Task<bool> SendAsync(PushTarget target, string payloadJson, CancellationToken ct = default)
        {
            Documents[target.Endpoint] = payloadJson;
            return Task.FromResult(false);
        }

        public JsonElement DocumentFor(long userId) =>
            JsonDocument.Parse(Documents[$"https://push.example.test/{userId}"]).RootElement;
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

    /// <summary>
    /// A scope with no logged user, which is what every multi-recipient dispatch in this app runs in (a
    /// scheduled job): <c>ScopeUserId</c> resolves to null and the user filter on <c>PushSubscription</c>
    /// passes every recipient's row. Under the default fake the dispatcher would see only user 999's
    /// subscription and the Slovak half of these tests would be asserting on an absent document.
    /// </summary>
    private sealed class BackgroundLoggedUserService : ILoggedUserService
    {
        public ClaimsPrincipal? LoggedUserPrincipal => null;
        public bool IsAuthenticated => false;
        public IEnumerable<string> GetRoles => [];
        public string GetEmail => throw new InvalidOperationException("No logged user in a background context.");
        public long GetUserId => throw new InvalidOperationException("No logged user in a background context.");
        public long? GetUserIdOrNull => null;
    }
}
