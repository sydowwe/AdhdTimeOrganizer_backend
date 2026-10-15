using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using AdhdTimeOrganizer.application.service.user;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.Core.infrastructure.security;
using AdhdTimeOrganizer.infrastructure.jobs;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sydowwe.Framework.Contracts.notification;
using Sydowwe.Framework.Contracts.notification.payload;
using Sydowwe.Framework.Contracts.scheduling;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.domain.extServiceContract.user;
using Sydowwe.Framework.domain.helper;
using Sydowwe.Framework.infrastructure.extService.user;
using Sydowwe.Framework.infrastructure.persistence.seeder;
using Sydowwe.Framework.Testing;
using Sydowwe.Notifications.application.job;
using Sydowwe.Notifications.domain.entity;
using Sydowwe.Notifications.infrastructure;
using Sydowwe.Notifications.infrastructure.email;
using Sydowwe.Notifications.infrastructure.webPush;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// P3-07 — account deletion with a grace period. <c>DELETE user/account</c> schedules; the account can be kept by the
/// mailed link or from a web sign-in; trackers are refused meanwhile; nothing is pushed or mailed to the user; and
/// <see cref="AccountDeletionJobHandler"/> deletes the account once the date has passed.
/// <para>
/// Every part of it fails silently — a schedule that deletes on the spot, a link that works twice or after the date, a
/// suppression reader dropped from DI, a purge that skips a table or a kept account it should have spared — so every
/// case asserts on rows, on the mail the real sender built, or on what the fake push and mail senders were handed. The
/// erasure itself (every user-scoped table, the reminder fan-out) is pinned in <c>UserAccountDeletionTests</c>, which
/// drives the same purge.
/// </para>
/// </summary>
[Collection("Postgres")]
public class AccountDeletionGraceTests(AppDbContextFixture fixture) : AuthTestBase(fixture)
{
    private static string PageUrl => Helper.GetEnvVar("PAGE_URL");

    private static readonly KeyValuePair<string, string?>[] PushAndEmailOn =
    [
        new($"{EmailNotificationOptions.SectionName}:Enabled", "true"),
        new($"{PushNotificationOptions.SectionName}:VapidSubject", "mailto:test@example.com"),
        new($"{PushNotificationOptions.SectionName}:VapidPublicKey", "test-public-key"),
        new($"{PushNotificationOptions.SectionName}:VapidPrivateKey", "test-private-key")
    ];

    // ---- scheduling -------------------------------------------------------------------------------

    [Fact(DisplayName = "P3-07: scheduling deletes nothing, revokes every session, drops push subscriptions and mails a keep link")]
    public async Task Schedule_DeletesNothing_ClosesEveryChannel_AndMailsTheDate()
    {
        var (userId, email) = await CreateRealUserAsync(Fixture, "grace-schedule", TestPassword);
        long todoListId;
        await using (var db = (AppDbContext)Fixture.CreateDbContext())
        {
            db.Add(PushSubscriptionOf(userId));
            await db.SaveChangesAsync(CancellationToken);
            todoListId = (await UserDataInventory.SeedEverySliceAsync(db, userId, CancellationToken)).TodoListId;
        }

        // A real web session and a real tracker session, so "every session" is checked against both kinds.
        using (var web = CreateCookieClient())
            await LoginAsync(web, email, TestPassword);
        (await ExtensionLoginAsync(email)).StatusCode.Should().Be(HttpStatusCode.OK);

        var before = DateTime.UtcNow;
        var (deletesAt, keepToken, mail) = await ScheduleCapturingMailAsync(userId);

        deletesAt.UtcDateTime.Should().BeCloseTo(before + AccountDeletionScheduleService.GracePeriod, TimeSpan.FromMinutes(1));

        await using (var db = (AppDbContext)Fixture.CreateDbContext())
        {
            var user = await db.Users.IgnoreQueryFilters().AsNoTracking().SingleAsync(u => u.Id == userId, CancellationToken);
            user.DeletionScheduledFor.Should().BeCloseTo(deletesAt.UtcDateTime, TimeSpan.FromSeconds(1));
            user.DeletionKeepTokenHash.Should().NotBeNullOrEmpty().And.NotBe(keepToken, "only a hash of the token is stored");

            (await db.TodoLists.IgnoreQueryFilters().AnyAsync(l => l.Id == todoListId, CancellationToken))
                .Should().BeTrue("scheduling deletes nothing");
            (await db.RefreshTokens.IgnoreQueryFilters().AnyAsync(r => r.UserId == userId && !r.IsRevoked, CancellationToken))
                .Should().BeFalse("every session, the tracker's included, is revoked with the schedule");
            (await db.Set<PushSubscription>().AnyAsync(s => s.UserId == userId, CancellationToken))
                .Should().BeFalse("nothing may buzz during the grace period");
        }

        mail.To.Should().Be(email);
        mail.Html.Should().Contain(deletesAt.ToString("d MMM yyyy, HH:mm", System.Globalization.CultureInfo.InvariantCulture),
            "the mail states the date, in the user's own zone (UTC here)");
    }

    [Fact(DisplayName = "P3-07: asking again while a deletion is pending never pushes the date out")]
    public async Task Schedule_Twice_KeepsTheEarlierDate()
    {
        var (userId, _) = await CreateRealUserAsync(Fixture, "grace-twice", TestPassword);

        var (first, _, _) = await ScheduleCapturingMailAsync(userId);
        var earlier = first.UtcDateTime - TimeSpan.FromDays(3);
        await SetScheduleAsync(userId, earlier);

        var (second, secondToken, _) = await ScheduleCapturingMailAsync(userId);

        second.UtcDateTime.Should().BeCloseTo(earlier, TimeSpan.FromSeconds(1), "the earlier date stands");
        (await KeepWithTokenAsync(userId, secondToken)).StatusCode.Should().Be(HttpStatusCode.NoContent,
            "the second request mails a fresh link, and that one works");
    }

    // ---- keeping by the link ----------------------------------------------------------------------

    [Fact(DisplayName = "P3-07: the mailed link keeps the account once, and is refused the second time")]
    public async Task KeepLink_WorksOnce()
    {
        var (userId, _) = await CreateRealUserAsync(Fixture, "grace-link", TestPassword);
        var (_, keepToken, _) = await ScheduleCapturingMailAsync(userId);

        (await KeepWithTokenAsync(userId, keepToken + "x")).StatusCode.Should().Be(HttpStatusCode.BadRequest, "a wrong token");
        (await KeepWithTokenAsync(userId + 1, keepToken)).StatusCode.Should().Be(HttpStatusCode.BadRequest, "another user's id");
        (await ScheduledForAsync(userId)).Should().NotBeNull("a refused link changes nothing");

        (await KeepWithTokenAsync(userId, keepToken)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ScheduledForAsync(userId)).Should().BeNull("the link keeps the account");

        (await KeepWithTokenAsync(userId, keepToken)).StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "the link is single-use");
    }

    [Fact(DisplayName = "P3-07: the mailed link is dead once the deletion date has passed")]
    public async Task KeepLink_AfterTheDate_IsRefused()
    {
        var (userId, _) = await CreateRealUserAsync(Fixture, "grace-link-late", TestPassword);
        var (_, keepToken, _) = await ScheduleCapturingMailAsync(userId);
        await SetScheduleAsync(userId, DateTime.UtcNow - TimeSpan.FromMinutes(1));

        (await KeepWithTokenAsync(userId, keepToken)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ScheduledForAsync(userId)).Should().NotBeNull("the purge will take it on its next run");
    }

    // ---- signing in during the grace period -------------------------------------------------------

    [Fact(DisplayName = "P3-07: a web sign-in during the grace period works, says when, and does not keep the account by itself")]
    public async Task WebSignIn_OffersToKeep_AndKeepingClearsTheSchedule()
    {
        var (userId, email) = await CreateRealUserAsync(Fixture, "grace-web", TestPassword);
        var (deletesAt, _, _) = await ScheduleCapturingMailAsync(userId);

        using var web = CreateCookieClient();
        await LoginAsync(web, email, TestPassword);

        (await ReadScheduleFromUserDataAsync(web)).Should().BeCloseTo(deletesAt.UtcDateTime, TimeSpan.FromSeconds(1),
            "the web app reads the date off /user/data and asks whether to keep the account");
        (await ScheduledForAsync(userId)).Should().NotBeNull(
            "signing in is not consent to keep — the user may be here only to export their data");

        (await web.PostAsync("user/account/keep", null, CancellationToken)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await ScheduledForAsync(userId)).Should().BeNull();
        (await ReadScheduleFromUserDataAsync(web)).Should().BeNull();
    }

    [Fact(DisplayName = "P3-07: a tracker cannot sign in during the grace period, and can again once the account is kept")]
    public async Task TrackerSignIn_IsRefusedDuringTheGracePeriod()
    {
        var (userId, email) = await CreateRealUserAsync(Fixture, "grace-tracker", TestPassword);
        await ScheduleCapturingMailAsync(userId);

        var refused = await ExtensionLoginAsync(email);
        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden, "a tracker cannot show the keep-my-account step");
        (await refused.Content.ReadAsStringAsync(CancellationToken))
            .Should().Contain("\"errorType\":\"AccountDeletionScheduled\"", "the tracker can tell this refusal from the others");

        await using (var db = (AppDbContext)Fixture.CreateDbContext())
            (await db.RefreshTokens.IgnoreQueryFilters().AnyAsync(r => r.UserId == userId && !r.IsRevoked, CancellationToken))
                .Should().BeFalse("the refusal issues no session");

        using (var web = CreateCookieClient())
        {
            await LoginAsync(web, email, TestPassword);
            (await web.PostAsync("user/account/keep", null, CancellationToken)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        (await ExtensionLoginAsync(email)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ---- the purge --------------------------------------------------------------------------------

    /// <summary>
    /// Three accounts against the same two runs: one due, one kept, one scheduled later. The due one has a web session
    /// opened during the grace period, which must die with it — the purge reports the sessions it ends, the way the
    /// immediate delete did.
    /// </summary>
    [Fact(DisplayName = "P3-07: the purge deletes after the date, spares a kept account and a later one, and ends the sessions")]
    public async Task Purge_DeletesOnlyWhatIsDue()
    {
        var (dueId, dueEmail) = await CreateRealUserAsync(Fixture, "grace-purge-due", TestPassword);
        var (keptId, _) = await CreateRealUserAsync(Fixture, "grace-purge-kept", TestPassword);
        var (laterId, _) = await CreateRealUserAsync(Fixture, "grace-purge-later", TestPassword);

        var (deletesAt, _, _) = await ScheduleCapturingMailAsync(dueId);
        var (_, keptToken, _) = await ScheduleCapturingMailAsync(keptId);
        (await KeepWithTokenAsync(keptId, keptToken)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        await ScheduleCapturingMailAsync(laterId);
        await SetScheduleAsync(laterId, deletesAt.UtcDateTime + TimeSpan.FromDays(2));

        using var rawClient = CreateRawClient();
        var graceSession = await LoginCapturingCookiesAsync(rawClient, dueEmail);

        await AccountDeletionPurgeRunner.RunAsync(Fixture.UnauthenticatedFactory.Services, deletesAt - TimeSpan.FromHours(1), CancellationToken);
        (await UserExistsAsync(dueId)).Should().BeTrue("it is not due yet");

        await AccountDeletionPurgeRunner.RunAsync(Fixture.UnauthenticatedFactory.Services, deletesAt + TimeSpan.FromMinutes(1), CancellationToken);

        (await UserExistsAsync(dueId)).Should().BeFalse();
        (await UserExistsAsync(keptId)).Should().BeTrue("a kept account is never deleted");
        (await UserExistsAsync(laterId)).Should().BeTrue("its date has not come");
        (await UserExistsAsync(FakeLoggedUserService.TestUserId)).Should().BeTrue();

        using var probe = new HttpRequestMessage(HttpMethod.Get, "timer-preset");
        probe.Headers.Add("Cookie", graceSession);
        (await rawClient.SendAsync(probe, CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "a session opened during the grace period dies with the account");
    }

    /// <summary>
    /// The immediate delete never exempted the seeded root admin — nothing in the framework base or its pre-processor
    /// looks at it — so neither does the grace period. Unlike the unconfirmed-account purge, which deletes accounts
    /// nobody asked about, this one only ever deletes an account whose owner asked for it.
    /// </summary>
    [Fact(DisplayName = "P3-07: the root admin is scheduled and deleted like anyone else, as the immediate delete treated it")]
    public async Task RootAdmin_IsNotExempt()
    {
        var rootEmail = Environment.GetEnvironmentVariable("ROOT_ADMIN_EMAIL")!;
        var rootId = await CreateUserWithEmailAsync(rootEmail);
        await using (var scope = Fixture.UnauthenticatedFactory.Services.CreateAsyncScope())
            (await scope.ServiceProvider.GetRequiredService<ISeedUserProvider>().GetRootAdminUserIdAsync(CancellationToken))
                .Should().Be(rootId, "otherwise this test proves nothing about the root admin");

        var (deletesAt, _, _) = await ScheduleCapturingMailAsync(rootId);
        await AccountDeletionPurgeRunner.RunAsync(Fixture.UnauthenticatedFactory.Services, deletesAt + TimeSpan.FromMinutes(1), CancellationToken);

        (await UserExistsAsync(rootId)).Should().BeFalse();
    }

    // ---- nothing reaches the user meanwhile -------------------------------------------------------

    /// <summary>
    /// The routine digest, which both users opted into by e-mail (it is off by default). Each also has a push
    /// subscription — for the scheduled user, one a web sign-in during the grace period registered after scheduling
    /// deleted the others. The control user proves the sends that did go out are real.
    /// </summary>
    [Fact(DisplayName = "P3-07: nothing is pushed or mailed to an account scheduled for deletion; the bell row is still written")]
    public async Task Suppression_LiveSend()
    {
        var (scheduledId, scheduledEmail, controlEmail, push, email, factory) = await SuppressionSetupAsync();
        await using var _ = factory;

        await using (var scope = factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<INotificationService>().NotifyAsync(
                NotificationRecipients.Users([scheduledId, FakeLoggedUserService.TestUserId]),
                new RoutineSuggestedTodayPayload(DateOnly.FromDateTime(DateTime.UtcNow), Count: 1),
                CancellationToken);

        push.Endpoints.Should().BeEquivalentTo([EndpointOf(FakeLoggedUserService.TestUserId)]);
        email.Recipients.Should().BeEquivalentTo([controlEmail]).And.NotContain(scheduledEmail);

        await using var db = CreateDbContext();
        var row = await db.Set<Notification>().SingleAsync(n => n.UserId == scheduledId, CancellationToken);
        row.DeferredUntil.Should().BeNull("a suppressed row is never handed to the quiet-hours flush");
    }

    [Fact(DisplayName = "P3-07: the quiet-hours flush releases nothing to an account scheduled for deletion since")]
    public async Task Suppression_DeferredFlush()
    {
        var (scheduledId, _, controlEmail, push, email, factory) = await SuppressionSetupAsync();
        await using var _ = factory;

        var flushAt = DateTimeOffset.UtcNow;
        await using (var db = CreateDbContext())
        {
            foreach (var userId in new[] { scheduledId, FakeLoggedUserService.TestUserId })
                db.Add(new Notification
                {
                    UserId = userId,
                    Type = NotificationType.RoutineSuggestedToday,
                    PayloadJson = """{"count":1}""",
                    DeferredUntil = flushAt.AddMinutes(-1)
                });
            await db.SaveChangesAsync(CancellationToken);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetServices<IScheduledJobHandler>()
                .OfType<FlushDeferredNotificationsJobHandler>()
                .Single(h => h.Key == FlushDeferredNotificationsJobHandler.HandlerKey)
                .FlushDeferredAsync(flushAt, CancellationToken);

        push.Endpoints.Should().BeEquivalentTo([EndpointOf(FakeLoggedUserService.TestUserId)]);
        email.Recipients.Should().BeEquivalentTo([controlEmail]);
    }

    // ---- helpers ----------------------------------------------------------------------------------

    /// <summary>
    /// <c>DELETE user/account</c> as <paramref name="userId"/>, through the real mail sender stopped at the SMTP hop:
    /// the date from the response, and the token from the link in the mail.
    /// </summary>
    private async Task<(DateTimeOffset DeletesAt, string KeepToken, SentMail Mail)> ScheduleCapturingMailAsync(long userId)
    {
        var mail = new CapturingUserEmailSender();
        await using var factory = CreateFactory(TestRoles.User, userId, services =>
        {
            services.RemoveAll<IUserEmailSenderService<User>>();
            services.AddSingleton<IUserEmailSenderService<User>>(mail);
        });

        using var request = new HttpRequestMessage(HttpMethod.Delete, "api/user/account")
        {
            Content = JsonContent.Create(new { Password = TestPassword })
        };
        var response = await factory.CreateClient().SendAsync(request, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        var deletesAt = new DateTimeOffset(body.GetProperty("deletionScheduledFor").GetDateTime().ToUniversalTime(), TimeSpan.Zero);

        var sent = mail.Sent.Should().ContainSingle().Subject;
        var match = Regex.Match(sent.Html, $"href=\"{Regex.Escape(PageUrl)}/keep-account\\?([^\"]+)\"");
        match.Success.Should().BeTrue("the mail links the keep-account page");
        var query = QueryHelpers.ParseQuery(WebUtility.HtmlDecode(match.Groups[1].Value));
        long.Parse(query["userId"]!).Should().Be(userId);

        return (deletesAt, query["token"]!, sent);
    }

    private async Task<HttpResponseMessage> KeepWithTokenAsync(long userId, string token)
    {
        using var client = CreateRawClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "auth/account/keep");
        // Its own throttle bucket, so the tests in this class do not share one.
        request.Headers.Add("X-Forwarded-For", Guid.NewGuid().ToString());
        request.Content = JsonContent.Create(new { UserId = userId, Token = token });
        return await client.SendAsync(request, CancellationToken);
    }

    private async Task<HttpResponseMessage> ExtensionLoginAsync(string email)
    {
        using var client = CreateRawClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "auth/extension/login");
        request.Headers.Add("X-Forwarded-For", Guid.NewGuid().ToString());
        request.Content = JsonContent.Create(new { Email = email, Password = TestPassword });
        return await client.SendAsync(request, CancellationToken);
    }

    private async Task<DateTime?> ReadScheduleFromUserDataAsync(HttpClient client)
    {
        var body = await client.GetFromJsonAsync<JsonElement>("user/data", CancellationToken);
        return body.TryGetProperty("deletionScheduledFor", out var value) && value.ValueKind != JsonValueKind.Null
            ? value.GetDateTime().ToUniversalTime()
            : null;
    }

    private static async Task<string> LoginCapturingCookiesAsync(HttpClient rawClient, string email)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "auth/login");
        request.Headers.Add("X-Client-Id", Guid.NewGuid().ToString());
        request.Content = JsonContent.Create(new
        {
            Email = email, Password = TestPassword, StayLoggedIn = false, RecaptchaToken = "test", Timezone = "UTC"
        });
        var response = await rawClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        return string.Join("; ", response.Headers.GetValues("Set-Cookie")
            .Select(c => c.Split(';', 2)[0])
            .Where(c => c.StartsWith($"{AuthCookies.AuthTokenName}=", StringComparison.Ordinal)
                        || c.StartsWith($"{AuthCookies.RefreshTokenName}=", StringComparison.Ordinal)));
    }

    private async Task SetScheduleAsync(long userId, DateTime deletesAt)
    {
        await using var db = (AppDbContext)Fixture.CreateDbContext();
        await db.Users.IgnoreQueryFilters().Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.DeletionScheduledFor, deletesAt), CancellationToken);
    }

    private async Task<DateTime?> ScheduledForAsync(long userId)
    {
        await using var db = (AppDbContext)Fixture.CreateDbContext();
        return await db.Users.IgnoreQueryFilters().Where(u => u.Id == userId)
            .Select(u => u.DeletionScheduledFor).SingleAsync(CancellationToken);
    }

    private async Task<bool> UserExistsAsync(long userId)
    {
        await using var db = (AppDbContext)Fixture.CreateDbContext();
        return await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == userId, CancellationToken);
    }

    /// <summary>A confirmed account with the <c>User</c> role at a fixed address, which <see cref="AuthTestBase.CreateRealUserAsync"/> cannot give.</summary>
    private async Task<long> CreateUserWithEmailAsync(string email)
    {
        await using var db = (AppDbContext)Fixture.CreateDbContext();
        var user = new User
        {
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N"),
            Locale = AvailableLocales.En,
            Timezone = TimeZoneInfo.Utc
        };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, TestPassword);
        db.Users.Add(user);
        await db.SaveChangesAsync(CancellationToken);

        var roleId = await db.Roles.Where(r => r.Name == PortalRoleCatalog.User).Select(r => r.Id).FirstAsync(CancellationToken);
        db.UserRoles.Add(new IdentityUserRole<long> { UserId = user.Id, RoleId = roleId });
        await db.SaveChangesAsync(CancellationToken);
        return user.Id;
    }

    /// <summary>
    /// A user scheduled for deletion and the fixture's test user as the control, both with a push subscription and the
    /// routine digest opted into by e-mail; and a factory whose push and mail senders capture.
    /// </summary>
    private async Task<(long ScheduledId, string ScheduledEmail, string ControlEmail, CapturingWebPushSender Push,
        CapturingEmailSender Email, ITestClientFactory Factory)> SuppressionSetupAsync()
    {
        var (scheduledId, scheduledEmail) = await CreateRealUserAsync(Fixture, "grace-suppressed", TestPassword);
        await ScheduleCapturingMailAsync(scheduledId);

        await using (var db = CreateDbContext())
        {
            foreach (var userId in new[] { scheduledId, FakeLoggedUserService.TestUserId })
            {
                db.Add(PushSubscriptionOf(userId));
                db.Add(new NotificationPreference
                {
                    UserId = userId,
                    Type = NotificationType.RoutineSuggestedToday,
                    Channel = NotificationChannel.Email,
                    Enabled = true
                });
            }

            await db.SaveChangesAsync(CancellationToken);
        }

        var push = new CapturingWebPushSender();
        var email = new CapturingEmailSender();
        var factory = CreateFactory(
            TestRoles.AdminAndUser,
            configureServices: services =>
            {
                services.AddSingleton<IWebPushSender>(push);
                services.AddSingleton<INotificationEmailSender>(email);
                // The sweeps that send these run with nobody logged in; the fake user would narrow the dispatcher's
                // subscription and preference reads to itself.
                services.AddScoped<ILoggedUserService, BackgroundLoggedUserService>();
            },
            configOverrides: PushAndEmailOn);

        return (scheduledId, scheduledEmail, FakeLoggedUserService.TestUserEmail, push, email, factory);
    }

    private static PushSubscription PushSubscriptionOf(long userId) => new()
    {
        UserId = userId,
        Endpoint = EndpointOf(userId),
        P256dh = "test-p256dh",
        Auth = "test-auth"
    };

    private static string EndpointOf(long userId) => $"https://push.example.test/deletion-grace-{userId}";

    private sealed record SentMail(string To, string Subject, string Html);

    /// <summary>The real sender — template, addressing, link — stopped at the SMTP hop.</summary>
    private sealed class CapturingUserEmailSender() : UserEmailSenderService<User>(
        new ConfigurationBuilder().AddInMemoryCollection([new("Application:Name", "Test app")]).Build())
    {
        public ConcurrentQueue<SentMail> Sent { get; } = new();

        public override Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            Sent.Enqueue(new SentMail(email, subject, htmlMessage));
            return Task.CompletedTask;
        }
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

/// <summary>
/// Runs <see cref="AccountDeletionJobHandler"/> the way the Scheduler dispatcher does — one scope per fire, no ambient
/// user — with its clock pinned at <c>now</c>. Shared with <c>UserAccountDeletionTests</c>, which schedules through the
/// endpoint and then needs the deletion to actually happen.
/// </summary>
internal static class AccountDeletionPurgeRunner
{
    public static async Task RunAsync(IServiceProvider services, DateTimeOffset now, CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var handler = ActivatorUtilities.CreateInstance<AccountDeletionJobHandler>(scope.ServiceProvider, new FixedTimeProvider(now));

        await handler.ExecuteAsync(new ScheduledJobContext
        {
            ScheduledFireTimeUtc = now.UtcDateTime,
            ActualFireTimeUtc = now.UtcDateTime,
            JobKey = handler.Key,
            CorrelationId = "account-deletion-test",
            TriggerSource = TriggerSource.Manual
        }, ct);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
