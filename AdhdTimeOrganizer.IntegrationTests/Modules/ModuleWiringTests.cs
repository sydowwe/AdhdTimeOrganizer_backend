using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Notifications.application;
using AdhdTimeOrganizer.Notifications.application.dto;
using AdhdTimeOrganizer.Notifications.application.job;
using AdhdTimeOrganizer.Notifications.domain.entity;
using AdhdTimeOrganizer.Notifications.infrastructure;
using AdhdTimeOrganizer.Notifications.infrastructure.email;
using AdhdTimeOrganizer.Notifications.infrastructure.webPush;
using AdhdTimeOrganizer.Reminders.application.job;
using AdhdTimeOrganizer.Reminders.domain.entity;
using AdhdTimeOrganizer.Reminders.domain.@enum;
using AdhdTimeOrganizer.Reminders.domain.serviceContract;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.Scheduler.application.job;
using AdhdTimeOrganizer.Scheduler.domain.entity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sydowwe.Framework.Contracts.gdpr;
using Sydowwe.Framework.Contracts.notification;
using Sydowwe.Framework.Contracts.notification.payload;
using Sydowwe.Framework.Contracts.reminders;
using Sydowwe.Framework.Contracts.scheduling;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Modules;

/// <summary>
/// Composition-root tests for the Notifications and Reminders modules. Everything here is about the
/// <b>host wiring</b>, not module-internal behaviour (the modules carry their own logic and are covered by
/// their own docs/tests): the seams resolve out of a real request scope, the entities are in the schema, the
/// endpoints are routed, the boot registrars reach the Scheduler, and — the one that proves the two modules
/// are joined to each other and to this app — a due reminder actually dispatches into notification history.
/// <para>
/// These fail loudly for the class of mistake that never shows up as a compile error: a module service that
/// takes a plain <c>DbContext</c> with no alias registered, an options section that was never bound, an
/// endpoint assembly missing from FastEndpoints discovery, or a no-op fallback winning over a live seam.
/// </para>
/// </summary>
[Collection("Postgres")]
public class ModuleWiringTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long RecipientUserId = FakeLoggedUserService.TestUserId;

    /// <summary>
    /// Every MAIL_* env var the test fixture sets is present, so <see cref="EmailNotificationOptions"/>
    /// auto-detects SMTP as configured and the email channel is default-on for
    /// <see cref="NotificationType.DeadlineApproaching"/> — which would make the dispatcher open a socket to
    /// the fake SMTP host. Pin the master switch off for the dispatch tests; the channel's own behaviour is
    /// not what these tests are about.
    /// </summary>
    private static readonly KeyValuePair<string, string?>[] EmailOff =
        [new($"{EmailNotificationOptions.SectionName}:Enabled", "false")];

    // ---- DI seams -------------------------------------------------------------------------------

    /// <summary>
    /// Every cross-module contract resolves out of a real scope. Most of these are activated only by a
    /// background job or an inbound request, so a missing registration would otherwise surface at runtime in
    /// production rather than at boot.
    /// </summary>
    [Fact]
    public void ModuleSeams_AllResolveFromARequestScope()
    {
        using var scope = Fixture.AdminAndUserFactory.Services.CreateScope();
        var sp = scope.ServiceProvider;

        // Notifications
        Assert.NotNull(sp.GetRequiredService<INotificationService>());
        Assert.NotNull(sp.GetRequiredService<IDeferredNotificationDispatcher>());
        Assert.NotNull(sp.GetRequiredService<INotificationPayloadEnricher>());
        Assert.NotNull(sp.GetRequiredService<INotificationTextRenderer>());
        Assert.NotNull(sp.GetRequiredService<INotificationEmailSender>());
        Assert.NotNull(sp.GetRequiredService<IWebPushSender>());

        // Reminders
        Assert.NotNull(sp.GetRequiredService<IReminderRegistry>());
        Assert.NotNull(sp.GetRequiredService<IReminderExportService>());

        // Scheduler — the substrate both modules push their recurring jobs onto.
        Assert.NotNull(sp.GetRequiredService<IScheduler>());
        Assert.NotNull(sp.GetRequiredService<ICronEvaluator>());
    }

    /// <summary>
    /// The live quiet-hours reader must win. Reminders ships <c>NoQuietHoursReader</c> for hosts without the
    /// Notifications module; if that ever got auto-registered it would silently disable quiet hours
    /// everywhere — a defect with no error, no log line and no failing compile.
    /// </summary>
    [Fact]
    public void QuietHoursReader_ResolvesToTheLiveReader_NotTheNoOpFallback()
    {
        using var scope = Fixture.AdminAndUserFactory.Services.CreateScope();

        var reader = scope.ServiceProvider.GetRequiredService<IQuietHoursReader>();

        Assert.IsType<QuietHoursReader>(reader);
    }

    /// <summary>
    /// Both modules' job handlers are discoverable by key. The Scheduler dispatcher matches
    /// <c>RecurringJobRegistration.HandlerKey</c> against these at fire time, so a handler missing from DI is
    /// a job that fires into nothing.
    /// </summary>
    [Fact]
    public void ScheduledJobHandlers_AreAllDiscoverableByKey()
    {
        using var scope = Fixture.AdminAndUserFactory.Services.CreateScope();

        var keys = scope.ServiceProvider.GetServices<IScheduledJobHandler>().Select(h => h.Key).ToList();

        Assert.Contains(ReminderScanJobHandler.HandlerKey, keys);
        Assert.Contains(PurgeExpiredReminderLedgersJobHandler.HandlerKey, keys);
        Assert.Contains(FlushDeferredNotificationsJobHandler.HandlerKey, keys);
        Assert.Contains(PurgeExpiredNotificationHistoryJobHandler.HandlerKey, keys);
        Assert.Contains(PurgeExpiredRunLogsJobHandler.HandlerKey, keys);
        Assert.Contains(OverdueJobSweepJobHandler.HandlerKey, keys);
    }

    /// <summary>
    /// No module service is registered twice. The portal runs two marker-interface scans — an
    /// <c>AppDomain</c> sweep and an explicit module-assembly one — over the <b>same</b>
    /// <c>Sydowwe.Framework</c> lifetime interfaces, so an overlap re-registers every module service.
    /// A single resolution hides it (last wins); an <c>IEnumerable&lt;T&gt;</c> does not, and every one of
    /// these is consumed as a collection: the scan handler would dispatch each due occurrence twice, and
    /// each seeder would run twice. Nothing about it fails to compile, log or throw.
    /// </summary>
    [Fact]
    public void ModuleServices_AreRegisteredExactlyOnce()
    {
        using var scope = Fixture.AdminAndUserFactory.Services.CreateScope();
        var sp = scope.ServiceProvider;

        var duplicatedHandlers = sp.GetServices<IScheduledJobHandler>()
            .GroupBy(h => h.Key)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key} x{g.Count()}")
            .ToList();
        Assert.Empty(duplicatedHandlers);

        AssertRegisteredOnce<INotificationService>(sp);
        AssertRegisteredOnce<IQuietHoursReader>(sp);
        AssertRegisteredOnce<IReminderRegistry>(sp);
        AssertRegisteredOnce<IScheduler>(sp);
        AssertRegisteredOnce<ICronEvaluator>(sp);
        AssertRegisteredOnce<INotificationTextRenderer>(sp);
        AssertRegisteredOnce<IReminderExportService>(sp);
    }

    private static void AssertRegisteredOnce<TService>(IServiceProvider sp) where TService : class
    {
        var implementations = sp.GetServices<TService>().ToList();
        Assert.True(implementations.Count == 1,
            $"{typeof(TService).Name} resolved {implementations.Count} implementations: "
            + string.Join(", ", implementations.Select(i => i.GetType().Name)));
    }

    // ---- schema ---------------------------------------------------------------------------------

    /// <summary>
    /// Both modules' tables are in the model the host builds. Guards the
    /// <c>ApplyConfigurationsFromAssembly</c> calls in <c>AppDbContext.OnModelCreating</c> — drop one and the
    /// DbSets still compile, they just map to nothing.
    /// </summary>
    [Fact]
    public async Task ModuleEntities_AreMappedAndQueryable()
    {
        await using var db = CreateDbContext();

        Assert.Equal(0, await db.Set<Notification>().CountAsync(CancellationToken));
        Assert.Equal(0, await db.Set<NotificationPreference>().CountAsync(CancellationToken));
        Assert.Equal(0, await db.Set<NotificationQuietHours>().CountAsync(CancellationToken));
        Assert.Equal(0, await db.Set<PushSubscription>().CountAsync(CancellationToken));
        Assert.Equal(0, await db.Set<ReminderDefinition>().CountAsync(CancellationToken));
        Assert.Equal(0, await db.Set<ReminderRecipient>().CountAsync(CancellationToken));
        Assert.Equal(0, await db.Set<ReminderLeadOffset>().CountAsync(CancellationToken));
        Assert.Equal(0, await db.Set<ReminderDispatch>().CountAsync(CancellationToken));
        Assert.Equal(0, await db.Set<ReminderOccurrenceAction>().CountAsync(CancellationToken));
        Assert.Equal(0, await db.Set<ReminderKindPreference>().CountAsync(CancellationToken));
    }

    // ---- routing --------------------------------------------------------------------------------

    /// <summary>
    /// The module endpoints are routed. FastEndpoints discovery is pinned to an explicit assembly list in
    /// <c>Program.cs</c>; leaving a module assembly out of it produces no error at all, just 404s.
    /// A non-404 is the whole assertion — status beyond that is each endpoint's own business.
    /// </summary>
    [Theory]
    // Notifications
    [InlineData("GET", "/api/notification/mine")]
    [InlineData("GET", "/api/notification/unread-count")]
    [InlineData("POST", "/api/notification/read-all")]
    [InlineData("GET", "/api/notification-preference/mine")]
    [InlineData("GET", "/api/notification-quiet-hours")]
    [InlineData("GET", "/api/push-subscription/vapid-public-key")]
    // Reminders
    [InlineData("GET", "/api/reminder-preference")]
    [InlineData("POST", "/api/reminder-definition/register")]
    [InlineData("POST", "/api/reminder-dashboard/upcoming")]
    [InlineData("POST", "/api/reminder-dashboard/my-upcoming")]
    [InlineData("POST", "/api/reminder-dashboard/dispatch-history")]
    [InlineData("GET", "/api/reminder-dashboard/overview")]
    public async Task ModuleEndpoints_AreRouted(string method, string route)
    {
        var client = CreateClient();

        var response = await client.SendAsync(
            new HttpRequestMessage(new HttpMethod(method), route) { Content = JsonContent.Create(new { }) },
            CancellationToken);

        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// The VAPID public key endpoint reports the deployment's real push-configuration state, because the SPA
    /// branches on it: a key means "subscribe", null means "skip Web Push". Two things could break that and
    /// neither is a compile error — the <c>PushNotification</c> section never being bound (which would make a
    /// configured deployment look unconfigured and silently disable push), or the endpoint answering with a
    /// key the sender cannot actually use. The test environment ships no VAPID credentials, so the null case
    /// is the ambient one; the configured case is forced through a config override.
    /// </summary>
    [Fact]
    public async Task VapidPublicKey_IsNullUntilConfigured()
    {
        var unconfigured = await CreateClient()
            .GetFromJsonAsync<VapidPublicKeyResponse>("/api/push-subscription/vapid-public-key", CancellationToken);
        Assert.Null(unconfigured!.PublicKey);

        await using var factory = CreateFactory(TestRoles.AdminAndUser, configOverrides:
        [
            new($"{PushNotificationOptions.SectionName}:VapidPublicKey", "test-public-key"),
            new($"{PushNotificationOptions.SectionName}:VapidPrivateKey", "test-private-key")
        ]);

        var configured = await factory.CreateClient()
            .GetFromJsonAsync<VapidPublicKeyResponse>("/api/push-subscription/vapid-public-key", CancellationToken);
        Assert.Equal("test-public-key", configured!.PublicKey);
    }

    /// <summary>
    /// The Notifications hub is mapped and authenticating. It is the module's live (InApp) channel and the
    /// only transport not routed through FastEndpoints — <c>MapHub</c> sits outside that pipeline, so
    /// nothing else in this file would notice it going missing. Negotiate is the handshake the SPA opens
    /// with; an authenticated caller must get past the <c>[Authorize]</c> on the hub, and an anonymous one
    /// must not.
    /// </summary>
    [Fact]
    public async Task NotificationHub_IsMappedAndAuthenticated()
    {
        var response = await CreateClient().PostAsync("/hubs/notifications/negotiate?negotiateVersion=1", null, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var anonymous = await CreateUnauthenticatedClient()
            .PostAsync("/hubs/notifications/negotiate?negotiateVersion=1", null, CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    // ---- boot reconciliation --------------------------------------------------------------------

    /// <summary>
    /// The two hosted registrars push their recurring jobs onto the Scheduler at boot. This has to be
    /// re-observed on a fresh host: the cached fixture factories started before the per-test Respawn wiped
    /// their rows, which is also exactly why re-registering every boot is required (Quartz's RAM job store
    /// drops all triggers on restart).
    /// </summary>
    [Fact]
    public async Task BootRegistrars_RegisterBothModulesRecurringJobs()
    {
        await using var factory = CreateFactory(TestRoles.AdminAndUser);
        // Starting the server is what runs the IHostedServices.
        factory.CreateClient().Dispose();

        await using var db = CreateDbContext();
        var jobKeys = await db.Set<ScheduledJob>().Select(j => j.JobKey).ToListAsync(CancellationToken);

        Assert.Contains(ReminderScanJobHandler.HandlerKey, jobKeys);
        Assert.Contains(PurgeExpiredReminderLedgersJobHandler.HandlerKey, jobKeys);
        Assert.Contains(FlushDeferredNotificationsJobHandler.HandlerKey, jobKeys);
        Assert.Contains(PurgeExpiredNotificationHistoryJobHandler.HandlerKey, jobKeys);
    }

    // ---- GDPR erasure fan-out --------------------------------------------------------------------

    /// <summary>
    /// Both modules' erasers are reachable through the <c>Sydowwe.Framework.Contracts</c> fan-out that <c>DeleteUserAccountEndpoint</c>
    /// drives. Resolving an empty enumerable is a valid state for a host shipping neither module, which is
    /// exactly why it cannot be left to fail loudly — this app ships both.
    /// </summary>
    [Fact]
    public void SubjectDataErasers_BothModulesAreInTheFanOut()
    {
        using var scope = Fixture.AdminAndUserFactory.Services.CreateScope();

        var moduleKeys = scope.ServiceProvider.GetServices<ISubjectDataEraser>().Select(e => e.ModuleKey).ToList();

        Assert.Contains("Notifications", moduleKeys);
        Assert.Contains("Reminders", moduleKeys);
    }

    /// <summary>
    /// The fan-out clears the rows no cascade reaches. The host FKs cascade
    /// <c>Notification</c>/<c>NotificationPreference</c>/<c>PushSubscription</c> off the user row; these
    /// FK-free tables are the ones that would otherwise survive a deleted account forever.
    /// <para>
    /// Driven through the erasers directly rather than the HTTP endpoint: <c>DELETE /user/account</c> sits
    /// behind <c>VerifyUserPreProcessor</c> (current password + a valid 2FA token), which is a test of the
    /// auth pre-processor, not of this wiring. Committing here also stands in for the endpoint's commit,
    /// which is <c>UserManager.DeleteAsync</c>'s own <c>SaveChanges</c> on this same scoped context.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SubjectDataErasers_ClearTheRowsNoCascadeReaches()
    {
        await using var scope = Fixture.AdminAndUserFactory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.Set<NotificationQuietHours>().Add(new NotificationQuietHours
        {
            UserId = RecipientUserId, StartMinute = 22 * 60, EndMinute = 7 * 60
        });
        db.Set<ReminderKindPreference>().Add(new ReminderKindPreference
        {
            UserId = RecipientUserId, OwnerModule = "Portal", Kind = "DueNow", Enabled = false
        });
        await db.SaveChangesAsync(CancellationToken);

        foreach (var eraser in scope.ServiceProvider.GetServices<ISubjectDataEraser>())
            await eraser.EraseSubjectDataAsync(new SubjectErasureRequest(RecipientUserId, RecipientUserId), CancellationToken);
        await db.SaveChangesAsync(CancellationToken);

        await using var verify = CreateDbContext();
        Assert.Equal(0, await verify.Set<NotificationQuietHours>().CountAsync(CancellationToken));
        Assert.Equal(0, await verify.Set<ReminderKindPreference>().CountAsync(CancellationToken));
    }

    // ---- end to end -----------------------------------------------------------------------------

    /// <summary>
    /// The Notifications module writes history through the <c>Sydowwe.Framework.Contracts</c> contract, with no authenticated user —
    /// the way every background caller reaches it.
    /// </summary>
    [Fact]
    public async Task NotifyAsync_WritesHistoryForTheRecipient()
    {
        await using var factory = CreateFactory(TestRoles.AdminAndUser, configOverrides: EmailOff);
        await using var scope = factory.Services.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<INotificationService>().NotifyAsync(
            NotificationRecipients.User(RecipientUserId),
            new TestNotificationPayload("wiring check"),
            CancellationToken);

        await using var db = CreateDbContext();
        var notification = await db.Set<Notification>().SingleAsync(CancellationToken);
        Assert.Equal(RecipientUserId, notification.UserId);
        Assert.Equal(NotificationType.Test, notification.Type);
        Assert.False(notification.IsRead);
    }

    /// <summary>
    /// The one that proves all three modules are joined to this app: register a reminder that is already due
    /// through the <c>Sydowwe.Framework.Contracts</c> registry, fire the Reminders scan handler, and the Notifications module ends up
    /// holding a history row for the recipient — plus a <c>Sent</c> row in the append-only dispatch ledger.
    /// </summary>
    [Fact]
    public async Task DueReminder_DispatchesThroughTheNotificationsModule()
    {
        await using var factory = CreateFactory(TestRoles.AdminAndUser, configOverrides: EmailOff);
        await using var scope = factory.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        // Registered for the future, then fired past it. The occurrence calculator only ever returns an
        // instant at or after "now", so registering something already overdue yields a null
        // NextOccurrenceAt and the scan would correctly never see it — that is the no-backfill-flood rule,
        // not a wiring failure. The scan takes "now" from the fire instant precisely so this is drivable.
        var dueAt = DateTimeOffset.UtcNow.AddMinutes(10);
        var registration = await sp.GetRequiredService<IReminderRegistry>().RegisterAsync(new ReminderRegistration
        {
            Key = new ReminderKey("Portal", "WiringSmoke", "1", "DueNow"),
            Schedule = ReminderSchedule.OneShot(dueAt, [0]),
            TemplateKey = "wiring-smoke",
            NotificationType = NotificationType.DeadlineApproaching,
            RecipientMode = RecipientMode.ExplicitUsers,
            ExplicitRecipientUserIds = [RecipientUserId]
        }, CancellationToken);

        Assert.True(registration.Created);

        var scan = sp.GetServices<IScheduledJobHandler>().Single(h => h.Key == ReminderScanJobHandler.HandlerKey);
        var fireTime = dueAt.UtcDateTime.AddMinutes(1);
        await scan.ExecuteAsync(new ScheduledJobContext
        {
            ScheduledFireTimeUtc = fireTime,
            ActualFireTimeUtc = fireTime,
            JobKey = ReminderScanJobHandler.HandlerKey,
            CorrelationId = "module-wiring-test",
            TriggerSource = TriggerSource.Manual
        }, CancellationToken);

        await using var db = CreateDbContext();

        var dispatch = await db.Set<ReminderDispatch>().SingleAsync(CancellationToken);
        Assert.Equal(registration.DefinitionId, dispatch.ReminderDefinitionId);
        Assert.Equal(DispatchOutcome.Sent, dispatch.Outcome);
        Assert.Equal(NotificationType.DeadlineApproaching, dispatch.NotificationTypeSnapshot);

        var notification = await db.Set<Notification>().SingleAsync(CancellationToken);
        Assert.Equal(RecipientUserId, notification.UserId);
        Assert.Equal(NotificationType.DeadlineApproaching, notification.Type);

        // One-shot with a single offset: nothing left to fire, so the definition terminalises rather than
        // staying Active with a stale NextOccurrenceAt the next scan would re-pick.
        var definition = await db.Set<ReminderDefinition>().SingleAsync(CancellationToken);
        Assert.Equal(ReminderStatus.Completed, definition.Status);
        Assert.Null(definition.NextOccurrenceAt);
    }
}