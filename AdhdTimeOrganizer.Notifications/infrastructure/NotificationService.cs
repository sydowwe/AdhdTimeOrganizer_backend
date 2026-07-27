using System.Collections.Concurrent;
using AdhdTimeOrganizer.Notifications.application;
using AdhdTimeOrganizer.Notifications.application.dto;
using AdhdTimeOrganizer.Notifications.domain.entity;
using AdhdTimeOrganizer.Notifications.infrastructure.email;
using AdhdTimeOrganizer.Notifications.infrastructure.realtime;
using AdhdTimeOrganizer.Notifications.infrastructure.webPush;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MojaDigitalnaFirma.Kernel.notification;
using MojaDigitalnaFirma.Kernel.notification.payload;
using Sydowwe.Framework.domain.entity.user;
using Sydowwe.Framework.domain.helper;
using Sydowwe.Framework.infrastructure.persistence;

namespace AdhdTimeOrganizer.Notifications.infrastructure;

public class NotificationService<TUser>(
    DbContext dbContext,
    UserManager<TUser> userManager,
    IHubContext<NotificationHub> hub,
    IWebPushSender webPushSender,
    INotificationTextRenderer renderer,
    INotificationPayloadEnricher payloadEnricher,
    INotificationEmailSender emailSender,
    IOptions<PushNotificationOptions> pushOptions,
    IOptions<EmailNotificationOptions> emailOptions,
    IQuietHoursReader quietHoursReader,
    IConfiguration configuration,
    ILogger<NotificationService<TUser>> logger) : INotificationService, IDeferredNotificationDispatcher
    where TUser : BaseUser
{
    /// <summary>
    /// The deployment-configured time zone (<c>Application:Timezone</c>) quiet-hours windows are interpreted
    /// in — the repo models no per-user zone. Resolved once per scope and cached; falls back to UTC so a
    /// misconfigured zone degrades the deferral rather than failing the send.
    /// </summary>
    private TimeZoneInfo? quietHoursTimeZone;

    private TimeZoneInfo QuietHoursTimeZone
    {
        get
        {
            if (quietHoursTimeZone is not null)
                return quietHoursTimeZone;

            try
            {
                return quietHoursTimeZone = Helper.GetTimezone(configuration);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Could not resolve the deployment time zone; quiet hours will be interpreted in UTC.");
                return quietHoursTimeZone = TimeZoneInfo.Utc;
            }
        }
    }

    public async Task NotifyAsync(NotificationRecipients recipients, NotificationType type, INotificationPayload? payload = null, CancellationToken ct = default)
    {
        var userIds = await ResolveRecipientsAsync(recipients, ct);
        if (userIds.Count == 0)
            return;

        var payloadJson = SerializePayload(payload);

        // Bulk-load all prefs for this type in one round-trip instead of per-recipient.
        var allPrefs = await dbContext.Set<NotificationPreference>().AsNoTracking()
            .Where(p => userIds.Contains(p.UserId) && p.Type == type)
            .ToListAsync(ct);

        var prefsByUser = allPrefs
            .GroupBy(p => p.UserId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var inAppIds = userIds
            .Where(id => IsEnabled(prefsByUser.GetValueOrDefault(id, []), type, NotificationChannel.InApp))
            .ToHashSet();
        var webPushIds = userIds
            .Where(id => IsEnabled(prefsByUser.GetValueOrDefault(id, []), type, NotificationChannel.WebPush))
            .ToHashSet();
        var emailIds = userIds
            .Where(id => IsEnabled(prefsByUser.GetValueOrDefault(id, []), type, NotificationChannel.Email))
            .ToHashSet();

        var activeIds = inAppIds.Union(webPushIds).Union(emailIds).ToList();
        if (activeIds.Count == 0)
            return;

        // Quiet hours (§52 ods. 10 ZP right-to-disconnect posture, notifications follow-up 05): a recipient
        // inside their window has the BACKGROUND channels withheld — a phone must not buzz at 22:30. InApp is
        // never deferred (a connected user is not disturbed by a bell they are already looking at) and neither
        // is the history row, so the notification is visible in-app immediately either way. Read here, before
        // the parallel fan-out, for the same DbContext reason as the email addresses below.
        var deferUntilByUser = await ResolveDeferralsAsync(webPushIds, emailIds, DateTimeOffset.UtcNow, ct);
        webPushIds.ExceptWith(deferUntilByUser.Keys);
        emailIds.ExceptWith(deferUntilByUser.Keys);

        string title, body;
        try
        {
            // Render from the ENRICHED payload, persist the raw one: display names are resolved per read and
            // never frozen into storage (payload minimization — see INotificationPayloadEnricher). One call
            // for the whole batch, since every recipient shares this payload.
            var enriched = await payloadEnricher.EnrichAsync([payloadJson], ct);
            (title, body) = renderer.Render(type, enriched[0]);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Notification render failed for type {Type}; skipping dispatch", type);
            return;
        }

        // Batch-insert one Notification per recipient — single SaveChanges instead of N transactions.
        var notifications = activeIds
            .Select(uid => new Notification
            {
                UserId = uid,
                Type = type,
                PayloadJson = payloadJson,
                DeferredUntil = deferUntilByUser.TryGetValue(uid, out var until) ? until : null
            })
            .ToList();
        await dbContext.AddRangeAsync(notifications, ct);
        await dbContext.SaveChangesAsync(ct);

        var notifByUser = notifications.ToDictionary(n => n.UserId);

        // Resolve email addresses BEFORE the parallel fan-out. UserManager is backed by this same scoped
        // DbContext (AddEntityFrameworkStores), and a DbContext cannot service two queries at once — if this
        // read ran concurrently with the Web Push branch's subscription read it would throw
        // "a second operation was started on this context". Reading here keeps the parallel region pure
        // network I/O, with only the Web Push branch touching the context (sequentially, within its task).
        var emailRecipients = await ResolveEmailRecipientsAsync(emailIds, ct);

        // PERF-1: all channels dispatched in parallel — none touch the DbContext except Web Push.
        var dispatchTasks = new List<Task>(3);
        if (inAppIds.Count > 0)
            dispatchTasks.Add(DispatchSignalRBulkAsync(inAppIds, notifByUser, title, body, ct));
        if (webPushIds.Count > 0)
            dispatchTasks.Add(DispatchWebPushBulkAsync(webPushIds, notifByUser, title, body, ct));
        if (emailRecipients.Count > 0)
            dispatchTasks.Add(DispatchEmailBulkAsync(emailRecipients, title, body, ct));
        await Task.WhenAll(dispatchTasks);
    }

    /// <summary>
    /// Serializes a payload record to the persisted camelCase document.
    /// <para>
    /// Two subtleties, both load-bearing. (1) <see cref="RawNotificationPayload"/> is already-stored JSON being
    /// rehydrated (the Reminders dispatch path) — it is written out verbatim, never wrapped in a
    /// <c>{"json": …}</c> envelope. (2) Everything else is serialized as <c>object</c> <b>on purpose</b>:
    /// <c>JsonHelper.Serialize&lt;T&gt;</c> serializes the <i>declared</i> type, so passing the
    /// <see cref="INotificationPayload"/>-typed variable directly would emit <c>{}</c> — the marker has no
    /// members. Declaring <c>object</c> makes System.Text.Json use the runtime type.
    /// </para>
    /// </summary>
    private static string SerializePayload(INotificationPayload? payload)
    {
        return payload switch
        {
            null => "{}",
            RawNotificationPayload raw => raw.Json?.ToJsonString() ?? "{}",
            _ => JsonHelper.Serialize<object>(payload)
        };
    }

    // PERF-2: fire all per-user SignalR sends in parallel — safe because hub client is thread-safe.
    private Task DispatchSignalRBulkAsync(
        IReadOnlyCollection<long> userIds,
        Dictionary<long, Notification> notifByUser,
        string title, string body, CancellationToken ct)
    {
        return Task.WhenAll(userIds.Select(uid => DispatchSignalRAsync(uid, notifByUser[uid], title, body, ct)));
    }

    private async Task DispatchSignalRAsync(long userId, Notification notification, string title, string body, CancellationToken ct)
    {
        var dto = new NotificationDto(notification.Id, notification.Type, title, body, notification.CreatedTimestamp, notification.IsRead);
        try
        {
            await hub.Clients.User(userId.ToString()).SendAsync(NotificationHub.ReceiveMethod, dto, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SignalR dispatch failed for user {UserId}", userId);
        }
    }

    private async Task DispatchWebPushBulkAsync(
        IReadOnlyCollection<long> userIds,
        Dictionary<long, Notification> notifByUser,
        string title, string body, CancellationToken ct)
    {
        if (!pushOptions.Value.IsConfigured)
            return;

        var subscriptions = await dbContext.Set<PushSubscription>()
            .Where(s => userIds.Contains(s.UserId))
            .ToListAsync(ct);
        if (subscriptions.Count == 0)
            return;

        // PERF-3: build payloads only for users that actually have subscriptions.
        var subscribedUserIds = subscriptions.Select(s => s.UserId).ToHashSet();
        var payloadByUser = subscribedUserIds.ToDictionary(uid => uid, uid =>
        {
            var n = notifByUser[uid];
            return JsonHelper.Serialize(new { title, body, data = new { id = n.Id, type = n.Type.ToString() } });
        });

        // PERF-3: bounded-concurrency parallel sends — DbContext not touched during sends.
        const int maxConcurrency = 10;
        using var semaphore = new SemaphoreSlim(maxConcurrency);
        var toPrune = new ConcurrentBag<PushSubscription>();

        await Task.WhenAll(subscriptions.Select(async sub =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                var gone = await webPushSender.SendAsync(new PushTarget(sub.Endpoint, sub.P256dh, sub.Auth), payloadByUser[sub.UserId], ct);
                if (gone)
                    toPrune.Add(sub);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Web push delivery failed for subscription {SubscriptionId}", sub.Id);
            }
            finally
            {
                semaphore.Release();
            }
        }));

        if (!toPrune.IsEmpty)
            await dbContext.DeleteRangeAsync(toPrune.ToList(), ct);
    }

    private readonly record struct EmailRecipient(long UserId, string Email);

    /// <summary>
    /// Resolves recipient addresses for the email channel. Runs sequentially (NOT inside the parallel
    /// dispatch) because UserManager shares the dispatcher's scoped DbContext with the Web Push branch.
    /// Returns empty when SMTP is unconfigured — the same short-circuit as unconfigured Web Push, so the
    /// SMTP sender is never constructed (its ctor throws on missing MAIL_* env vars).
    /// </summary>
    private async Task<List<EmailRecipient>> ResolveEmailRecipientsAsync(IReadOnlyCollection<long> userIds, CancellationToken ct)
    {
        if (userIds.Count == 0 || !emailOptions.Value.IsConfigured)
            return [];

        var idList = userIds.ToList();
        return await userManager.Users
            .Where(u => idList.Contains(u.Id) && u.Email != null && u.Email != "")
            .Select(u => new EmailRecipient(u.Id, u.Email!))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Third channel: SMTP. Reaches approvers who never install the PWA (and iOS users, who get no Web
    /// Push at all outside an installed PWA). No history row of its own — the row was already written; no
    /// DbContext access here (addresses were resolved up front), so this is safe to run in parallel.
    /// </summary>
    private async Task DispatchEmailBulkAsync(
        IReadOnlyCollection<EmailRecipient> recipients, string title, string body, CancellationToken ct)
    {
        var options = emailOptions.Value;
        var appName = configuration["Application:Name"] ?? "MojaDigitalnaFirma";
        var portalUrl = string.IsNullOrWhiteSpace(options.PortalUrl)
            ? configuration["Application:Domain"]
            : options.PortalUrl;
        var html = NotificationEmailBody.Build(appName, title, body, portalUrl);

        // Bounded concurrency, kept low: the SMTP sender opens a connection per message.
        using var semaphore = new SemaphoreSlim(Math.Max(1, options.MaxConcurrency));

        await Task.WhenAll(recipients.Select(async r =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                await emailSender.SendAsync(r.Email, title, html, ct);
            }
            catch (Exception ex)
            {
                // PII: log the user id only — never the address (see the logging rule in CLAUDE.md).
                logger.LogWarning(ex, "Email notification delivery failed for user {UserId}", r.UserId);
            }
            finally
            {
                semaphore.Release();
            }
        }));
    }

    /// <summary>
    /// Per-recipient quiet-hours deferral for the background channels: the instant each recipient's window
    /// ends, for the recipients currently inside one. Only users who would actually receive Web Push or Email
    /// are considered — stamping <c>DeferredUntil</c> on an InApp-only recipient would give the flush job a
    /// row with nothing to deliver.
    /// </summary>
    private async Task<Dictionary<long, DateTimeOffset>> ResolveDeferralsAsync(
        IReadOnlyCollection<long> webPushIds, IReadOnlyCollection<long> emailIds, DateTimeOffset now, CancellationToken ct)
    {
        var deferrals = new Dictionary<long, DateTimeOffset>();

        var backgroundIds = webPushIds.Concat(emailIds).Distinct().ToList();
        if (backgroundIds.Count == 0)
            return deferrals;

        var windows = await quietHoursReader.GetWindowsAsync(backgroundIds, ct);
        if (windows.Count == 0)
            return deferrals;

        foreach (var userId in backgroundIds)
            // Absent window ⇒ no quiet hours (opt-in). ResumeAt is null when `now` is outside the window,
            // so the window lookup and the "is it active right now" check are one call.
            if (windows.TryGetValue(userId, out var window)
                && QuietHoursPolicy.ResumeAt(window, now, QuietHoursTimeZone) is { } resumeAt)
                deferrals[userId] = resumeAt;

        return deferrals;
    }

    /// <inheritdoc />
    public async Task DispatchDeferredAsync(IReadOnlyCollection<Notification> notifications, CancellationToken ct)
    {
        if (notifications.Count == 0)
            return;

        var userIds = notifications.Select(n => n.UserId).Distinct().ToList();
        var types = notifications.Select(n => n.Type).Distinct().ToList();

        // Re-check preferences at flush time, not the ones that held when the row was written: a user who
        // switched a channel off during their quiet hours must not receive it when the window lifts.
        var prefsByUser = (await dbContext.Set<NotificationPreference>().AsNoTracking()
                .Where(p => userIds.Contains(p.UserId) && types.Contains(p.Type))
                .ToListAsync(ct))
            .GroupBy(p => p.UserId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var rendered = await RenderDeferredAsync(notifications, ct);

        // A type whose render threw is dropped rather than delivered with empty text (same rule as the live
        // path); the job still clears the column, so a permanently broken renderer cannot wedge the sweep.
        var deliverable = notifications.Where(n => rendered.ContainsKey((n.Type, n.PayloadJson))).ToList();

        var webPushItems = deliverable
            .Where(n => IsEnabled(prefsByUser.GetValueOrDefault(n.UserId, []), n.Type, NotificationChannel.WebPush))
            .ToList();
        var emailItems = deliverable
            .Where(n => IsEnabled(prefsByUser.GetValueOrDefault(n.UserId, []), n.Type, NotificationChannel.Email))
            .ToList();

        // Same discipline as NotifyAsync: every DbContext read happens up front, so the fan-out below is pure
        // network I/O and the two channels cannot race a second operation onto the shared context.
        var subscriptions = pushOptions.Value.IsConfigured && webPushItems.Count > 0
            ? await dbContext.Set<PushSubscription>()
                .Where(s => userIds.Contains(s.UserId))
                .ToListAsync(ct)
            : [];

        var emailByUser = (await ResolveEmailRecipientsAsync(emailItems.Select(n => n.UserId).Distinct().ToList(), ct))
            .ToDictionary(r => r.UserId, r => r.Email);

        var subscriptionsByUser = subscriptions.GroupBy(s => s.UserId).ToDictionary(g => g.Key, g => g.ToList());

        var dispatchTasks = new List<Task>(2);
        if (subscriptionsByUser.Count > 0)
            dispatchTasks.Add(DispatchDeferredWebPushAsync(webPushItems, subscriptionsByUser, rendered, ct));
        if (emailByUser.Count > 0)
            dispatchTasks.Add(DispatchDeferredEmailAsync(emailItems, emailByUser, rendered, ct));
        await Task.WhenAll(dispatchTasks);
    }

    /// <summary>
    /// Renders once per distinct <c>(type, payload)</c> across the batch — several recipients of the same
    /// event share one payload, and the enricher resolves the whole batch in a single seam round trip.
    /// Enrichment is best-effort: a background job has no ambient request scope, so a failure falls back to
    /// the raw payloads and the renderer's name-less text rather than dropping the delivery.
    /// </summary>
    private async Task<Dictionary<(NotificationType Type, string PayloadJson), (string Title, string Body)>> RenderDeferredAsync(
        IReadOnlyCollection<Notification> notifications, CancellationToken ct)
    {
        var distinct = notifications.Select(n => (n.Type, n.PayloadJson)).Distinct().ToList();
        var payloads = distinct.Select(d => d.PayloadJson).ToList();

        IReadOnlyList<string> enriched;
        try
        {
            enriched = await payloadEnricher.EnrichAsync(payloads, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Payload enrichment failed while flushing deferred notifications; rendering unenriched");
            enriched = payloads;
        }

        var rendered = new Dictionary<(NotificationType, string), (string, string)>();
        for (var i = 0; i < distinct.Count; i++)
            try
            {
                rendered[distinct[i]] = renderer.Render(distinct[i].Type, enriched[i]);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Deferred notification render failed for type {Type}; skipping delivery", distinct[i].Type);
            }

        return rendered;
    }

    private async Task DispatchDeferredWebPushAsync(
        IReadOnlyCollection<Notification> notifications,
        Dictionary<long, List<PushSubscription>> subscriptionsByUser,
        Dictionary<(NotificationType Type, string PayloadJson), (string Title, string Body)> rendered,
        CancellationToken ct)
    {
        // One send per (notification, device). Unlike the live path this cannot key payloads by user — a
        // single flush can carry several notifications for the same recipient, each with its own text.
        var sends = notifications
            .Where(n => subscriptionsByUser.ContainsKey(n.UserId))
            .SelectMany(n => subscriptionsByUser[n.UserId].Select(sub => (Notification: n, Subscription: sub)))
            .ToList();
        if (sends.Count == 0)
            return;

        const int maxConcurrency = 10;
        using var semaphore = new SemaphoreSlim(maxConcurrency);
        var toPrune = new ConcurrentBag<PushSubscription>();

        await Task.WhenAll(sends.Select(async send =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                var (title, body) = rendered[(send.Notification.Type, send.Notification.PayloadJson)];
                var payload = JsonHelper.Serialize(new
                {
                    title,
                    body,
                    data = new { id = send.Notification.Id, type = send.Notification.Type.ToString() }
                });

                var gone = await webPushSender.SendAsync(
                    new PushTarget(send.Subscription.Endpoint, send.Subscription.P256dh, send.Subscription.Auth), payload, ct);
                if (gone)
                    toPrune.Add(send.Subscription);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Deferred web push delivery failed for subscription {SubscriptionId}", send.Subscription.Id);
            }
            finally
            {
                semaphore.Release();
            }
        }));

        // Distinct: a dead device can surface once per notification in the same flush.
        if (!toPrune.IsEmpty)
            await dbContext.DeleteRangeAsync(toPrune.DistinctBy(s => s.Id).ToList(), ct);
    }

    private async Task DispatchDeferredEmailAsync(
        IReadOnlyCollection<Notification> notifications,
        Dictionary<long, string> emailByUser,
        Dictionary<(NotificationType Type, string PayloadJson), (string Title, string Body)> rendered,
        CancellationToken ct)
    {
        var options = emailOptions.Value;
        var appName = configuration["Application:Name"] ?? "MojaDigitalnaFirma";
        var portalUrl = string.IsNullOrWhiteSpace(options.PortalUrl)
            ? configuration["Application:Domain"]
            : options.PortalUrl;

        using var semaphore = new SemaphoreSlim(Math.Max(1, options.MaxConcurrency));

        await Task.WhenAll(notifications
            .Where(n => emailByUser.ContainsKey(n.UserId))
            .Select(async n =>
            {
                await semaphore.WaitAsync(ct);
                try
                {
                    var (title, body) = rendered[(n.Type, n.PayloadJson)];
                    await emailSender.SendAsync(emailByUser[n.UserId], title, NotificationEmailBody.Build(appName, title, body, portalUrl), ct);
                }
                catch (Exception ex)
                {
                    // PII: log the user id only — never the address (see the logging rule in CLAUDE.md).
                    logger.LogWarning(ex, "Deferred email notification delivery failed for user {UserId}", n.UserId);
                }
                finally
                {
                    semaphore.Release();
                }
            }));
    }

    private async Task<List<long>> ResolveRecipientsAsync(NotificationRecipients recipients, CancellationToken ct)
    {
        if (recipients.UserIds is not null)
            return recipients.UserIds.ToList();

        if (recipients.AllUsers)
            return await userManager.Users.Select(u => u.Id).ToListAsync(ct);

        if (recipients.Roles is { Count: > 0 } roles)
        {
            var roleNames = roles.Select(r => r.ToString()).ToList();
            // Distinct so a user holding several of the targeted roles is resolved once.
            return await (
                from ur in dbContext.Set<IdentityUserRole<long>>()
                join r in dbContext.Set<UserRole>() on ur.RoleId equals r.Id
                where roleNames.Contains(r.Name!)
                select ur.UserId
            ).Distinct().ToListAsync(ct);
        }

        return [];
    }

    /// <summary>
    /// An explicit preference row always wins, in both directions. With no row the per-(type, channel)
    /// default applies: all-on for InApp/WebPush, per-type for Email — see NotificationChannelDefaults.
    /// </summary>
    private static bool IsEnabled(List<NotificationPreference> prefs, NotificationType type, NotificationChannel channel)
    {
        var pref = prefs.FirstOrDefault(p => p.Channel == channel);
        return pref?.Enabled ?? NotificationChannelDefaults.IsEnabledByDefault(type, channel);
    }
}