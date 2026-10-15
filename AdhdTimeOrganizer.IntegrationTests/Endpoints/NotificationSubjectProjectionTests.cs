using AdhdTimeOrganizer.Routines.application.notification;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Sydowwe.Framework.Contracts.notification;
using Sydowwe.Framework.Contracts.notification.payload;
using Sydowwe.Framework.Contracts.scheduling;
using Sydowwe.Framework.domain.helper;
using Sydowwe.Framework.Testing;
using Sydowwe.Notifications.application.job;
using Sydowwe.Notifications.domain.entity;
using Sydowwe.Notifications.infrastructure;
using Sydowwe.Notifications.infrastructure.email;
using Sydowwe.Notifications.infrastructure.webPush;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// Backend ask B2: a notification says <b>which entity</b> it is about, so a click reaches the thing instead
/// of the list its type lives in.
/// <para>
/// Every assertion here is on the bytes that leave the server, never on a status code or a stored row —
/// because every way this can break is silent:
/// </para>
/// <list type="number">
/// <item>The reference is optional by design, so a resolver that stops resolving produces a bell list that is
/// still valid, still 200, and merely goes back to being a dead end. Absence is asserted only where absence
/// is the answer (a digest, a cross-module deadline), and presence everywhere else, always in pairs.</item>
/// <item>The subject must come off the <b>persisted</b> payload, not the
/// <see cref="INotificationPayloadEnricher"/>-overlaid one. Reading the wrong document still renders a
/// perfectly good notification — it just carries an enricher's display overlay to the network boundary and
/// quietly voids the payload PII contract.
/// <see cref="Subject_IsResolvedFromThePersistedPayload_NotTheEnrichedOne"/> makes an enricher lie and checks
/// which document won.</item>
/// <item>The two deliveries of one event (SignalR/bell and Web Push) are rendered by different code paths
/// and used to disagree about click-through by construction. If they drift again, both still deliver — the
/// user just lands somewhere different depending on whether their tab happened to be open.</item>
/// </list>
/// </summary>
[Collection("Postgres")]
public class NotificationSubjectProjectionTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long TestUserId = FakeLoggedUserService.TestUserId;

    /// <summary>SMTP off (the fixture sets every MAIL_* var, so the dispatcher would open a socket), push on.</summary>
    private static readonly KeyValuePair<string, string?>[] PushOn =
    [
        new($"{EmailNotificationOptions.SectionName}:Enabled", "false"),
        new($"{PushNotificationOptions.SectionName}:VapidSubject", "mailto:test@example.com"),
        new($"{PushNotificationOptions.SectionName}:VapidPublicKey", "test-public-key"),
        new($"{PushNotificationOptions.SectionName}:VapidPrivateKey", "test-private-key")
    ];

    // ---- (1) the bell list ------------------------------------------------------------------------

    /// <summary>
    /// The five shapes the answer takes, in one page. A reminder attached to a task points at the task (that
    /// is what the user acts on, and what <c>PlannerTaskId</c> was added for); a standalone one points at
    /// itself; a routine nudge points at the period. A digest and a cross-module deadline point at nothing —
    /// and that is the answer, not a gap: the digest is N occurrences by construction, and
    /// <c>DeadlineApproachingPayload</c> carries a producer-composed title precisely because there is no
    /// single entity behind it.
    /// </summary>
    [Fact(DisplayName = "The bell list carries the entity each notification is about, and omits it where there is none")]
    public async Task BellList_CarriesTheSubjectOfEachNotification()
    {
        var taskReminderId = await SeedNotificationAsync(NotificationType.PersonalReminder, new PersonalReminderPayload(482, "Finish the tax return", 91));
        var standaloneId = await SeedNotificationAsync(NotificationType.PersonalReminder, new PersonalReminderPayload(483, "Call the dentist"));
        var periodId = await SeedNotificationAsync(RoutinesNotificationTypes.RoutinePeriodEndingSoon, new RoutinePeriodEndingSoonPayload(77, "Weekly", 3, 8, 2, 5));
        var digestId = await SeedNotificationAsync(NotificationType.ReminderDigest, new ReminderDigestPayload(4));
        var deadlineId = await SeedNotificationAsync(NotificationType.DeadlineApproaching, new DeadlineApproachingPayload("Blíži sa termín"));

        var byId = await ReadBellListAsync();

        SubjectOf(byId[taskReminderId]).Should().Be(("plannerTask", 91L),
            "a reminder that names a task deep-links to the task — that is the thing the user acts on");
        SubjectOf(byId[standaloneId]).Should().Be(("reminder", 483L),
            "a standalone reminder has no task, so it points at itself rather than at the reminders list");
        SubjectOf(byId[periodId]).Should().Be(("routinePeriod", 77L));

        // Absent, not null: "no subject" and "a subject that failed to resolve" are the same thing to a
        // client, and an absent key is the cheaper of the two to ignore. Matches `tag` / `url` in the push
        // document, and is the shape the frontend seam was built against.
        byId[digestId].TryGetProperty("subject", out _).Should().BeFalse(
            "a digest is about N occurrences by construction — the list IS the specific target");
        byId[deadlineId].TryGetProperty("subject", out _).Should().BeFalse(
            "DeadlineApproaching is the cross-module kind: its payload carries a composed title and no id, deliberately");
    }

    /// <summary>
    /// The kind vocabulary is server-owned and <b>append-only</b>: a rename silently unlinks every
    /// already-persisted notification of that kind, in every consuming app, with no error anywhere — the
    /// client simply stops recognising the term and falls back to its type-level route. Pinning the wire
    /// spelling here is what turns that into a failing test instead of a quiet regression.
    /// </summary>
    [Fact(DisplayName = "Subject kinds are the vocabulary terms clients map, spelled exactly")]
    public void SubjectKinds_AreStableWireTerms()
    {
        NotificationSubjectKinds.Reminder.Should().Be("reminder");
        NotificationSubjectKinds.PlannerTask.Should().Be("plannerTask");
        NotificationSubjectKinds.RoutinePeriod.Should().Be("routinePeriod");
        NotificationSubjectKinds.ScheduledJobRun.Should().Be("scheduledJobRun");
        NotificationSubjectKinds.InventoryItem.Should().Be("inventoryItem");
    }

    // ---- (2) the persisted payload, not the enriched one -------------------------------------------

    /// <summary>
    /// The payload PII contract at the network boundary. The persisted document is non-PII by construction;
    /// the enricher's overlay is not, and the bell list already runs the enricher over the whole page to
    /// render title/body. So the subject must be resolved from <c>Notification.PayloadJson</c>, and reading
    /// the enriched string instead would look completely correct in every other test.
    /// <para>
    /// The fake enricher here does two things a real one never would — it rewrites <c>plannerTaskId</c> and
    /// adds a name — precisely so "which document did you read" has a visible answer. If the tampered id
    /// comes back, the code is reading the overlay.
    /// </para>
    /// </summary>
    [Fact(DisplayName = "The subject is resolved from the persisted payload, never the enricher's overlay")]
    public async Task Subject_IsResolvedFromThePersistedPayload_NotTheEnrichedOne()
    {
        var id = await SeedNotificationAsync(NotificationType.PersonalReminder, new PersonalReminderPayload(482, "Finish the tax return", 91));

        await using var factory = CreateFactory(
            TestRoles.AdminAndUser,
            configureServices: services => services.AddSingleton<INotificationPayloadEnricher, TamperingEnricher>());

        var byId = await ReadBellListAsync(factory.CreateClient());

        SubjectOf(byId[id]).Should().Be(("plannerTask", 91L),
            "the enricher rewrote plannerTaskId to 6666 — seeing it here means the subject was read off the overlay, which also carries display names");

        // The overlay did reach the renderer, so this is a real test of two documents and not of a no-op one.
        byId[id].GetProperty("body").GetString().Should().Be("Finish the tax return");
    }

    // ---- (3) the two deliveries agree --------------------------------------------------------------

    /// <summary>
    /// The same event, delivered twice: SignalR to an open tab, Web Push to a closed one. A click on either
    /// has to land in the same place, which is why both are fed by one resolver rather than by a route map on
    /// the client and a url mechanism on the server.
    /// <para>
    /// The push reference lives in the nested <c>data</c> object, next to the click handler's other inputs —
    /// <b>not</b> as a top-level <c>url</c>. That is the whole boundary decision made concrete: the server
    /// names the entity, the app owns the route. A move to the top level would be invisible to any row-level
    /// check, so it is asserted on the document bytes.
    /// </para>
    /// </summary>
    [Fact(DisplayName = "The push document carries the same subject as the bell, in data and not as a url")]
    public async Task PushDocument_CarriesTheSameSubjectAsTheBell()
    {
        var sender = new CapturingWebPushSender();

        await using var factory = CreateFactory(
            TestRoles.AdminAndUser,
            configureServices: services => services.AddSingleton<IWebPushSender>(sender),
            configOverrides: PushOn);

        await SeedPushSubscriptionAsync();
        var id = await SeedNotificationAsync(
            RoutinesNotificationTypes.RoutineStreakGraceExpiring,
            new RoutineStreakGraceExpiringPayload(77, "Weekly", DateTime.UtcNow.AddDays(1), 12),
            deferredUntil: DateTimeOffset.UtcNow.AddMinutes(-1));

        await using (var scope = factory.Services.CreateAsyncScope())
            await ResolveFlushHandler(scope.ServiceProvider).FlushDeferredAsync(DateTimeOffset.UtcNow, CancellationToken);

        var document = JsonDocument.Parse(sender.Payloads.Should().ContainSingle().Subject).RootElement;
        var data = document.GetProperty("data");
        data.GetProperty("id").GetInt64().Should().Be(id);

        var subject = data.GetProperty("subject");
        (subject.GetProperty("kind").GetString(), subject.GetProperty("id").GetInt64())
            .Should().Be(("routinePeriod", 77L));

        document.TryGetProperty("url", out _).Should().BeFalse(
            "the top-level url is a route the PRODUCER supplied (only TimerBoundary does); the notifications module never invents one, because it does not know any app's route table");

        // And the bell says the same thing about the same row — one resolver, two deliveries.
        var byId = await ReadBellListAsync();
        SubjectOf(byId[id]).Should().Be(("routinePeriod", 77L));
    }

    // ---- helpers ----------------------------------------------------------------------------------

    /// <summary>
    /// The bell list as raw JSON elements, keyed by notification id. Parsed rather than deserialized into the
    /// DTO on purpose: half of what is under test is a key being <b>absent</b>, which a typed read turns back
    /// into a null and hides.
    /// </summary>
    private async Task<Dictionary<long, JsonElement>> ReadBellListAsync(HttpClient? client = null)
    {
        var response = await (client ?? CreateClient()).GetAsync("api/notification/mine", CancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(CancellationToken);
        return JsonDocument.Parse(json).RootElement.EnumerateArray()
            .ToDictionary(e => e.GetProperty("id").GetInt64(), e => e.Clone());
    }

    private static (string? Kind, long Id) SubjectOf(JsonElement notification)
    {
        var subject = notification.GetProperty("subject");
        return (subject.GetProperty("kind").GetString(), subject.GetProperty("id").GetInt64());
    }

    /// <summary>
    /// One notification row, written straight to the table with the payload a real producer would persist —
    /// serialized through <c>SerializePayload</c>'s own rule (<c>object</c>, so System.Text.Json uses the
    /// runtime type; the declared marker interface has no members and would emit <c>{}</c>).
    /// </summary>
    private async Task<long> SeedNotificationAsync(NotificationType type, INotificationPayload payload, DateTimeOffset? deferredUntil = null)
    {
        await using var db = CreateDbContext();
        var notification = new Notification
        {
            UserId = TestUserId,
            Type = type,
            PayloadJson = JsonHelper.Serialize<object>(payload),
            DeferredUntil = deferredUntil
        };
        db.Add(notification);
        await db.SaveChangesAsync(CancellationToken);
        return notification.Id;
    }

    private async Task SeedPushSubscriptionAsync()
    {
        await using var db = CreateDbContext();
        db.Add(new PushSubscription
        {
            UserId = TestUserId,
            Endpoint = $"https://push.example.test/{TestUserId}",
            P256dh = "test-p256dh",
            Auth = "test-auth"
        });
        await db.SaveChangesAsync(CancellationToken);
    }

    /// <summary>
    /// The flush job out of <c>IEnumerable&lt;IScheduledJobHandler&gt;</c>, the way the Scheduler dispatcher
    /// reaches it — it is registered by interface, so asking for the concrete type resolves nothing.
    /// </summary>
    private static FlushDeferredNotificationsJobHandler ResolveFlushHandler(IServiceProvider services) =>
        services.GetServices<IScheduledJobHandler>()
            .OfType<FlushDeferredNotificationsJobHandler>()
            .Single(h => h.Key == FlushDeferredNotificationsJobHandler.HandlerKey);

    /// <summary>
    /// An enricher that overlays a display name (what a real one does) <i>and</i> rewrites an id (what none
    /// does) — the second half is the tell. Any code that resolves the subject off the enriched document
    /// reports task 6666 instead of 91.
    /// </summary>
    private sealed class TamperingEnricher : INotificationPayloadEnricher
    {
        public Task<IReadOnlyList<string>> EnrichAsync(IReadOnlyList<string> payloadJsons, CancellationToken ct = default)
        {
            IReadOnlyList<string> enriched = payloadJsons
                .Select(json =>
                {
                    var node = JsonNode.Parse(json)!.AsObject();
                    node["employeeName"] = "Overlay Name";
                    if (node.ContainsKey("plannerTaskId"))
                        node["plannerTaskId"] = 6666;
                    return node.ToJsonString();
                })
                .ToList();
            return Task.FromResult(enriched);
        }
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
}
