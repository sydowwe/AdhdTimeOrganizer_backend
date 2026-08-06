using System.Net;
using System.Net.Sockets;
using AdhdTimeOrganizer.Notifications.application.dto;
using AdhdTimeOrganizer.Notifications.domain.entity;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.helper;
using Sydowwe.Framework.domain.serviceContract;

namespace AdhdTimeOrganizer.Notifications.application.endpoint.pushSubscription;

/// <summary>Registers (or refreshes) a Web Push subscription for the current user/device.</summary>
public class SubscribePushEndpoint(DbContext dbContext, IAuditService auditService) : Endpoint<SubscribePushRequest>
{
    public override void Configure()
    {
        Post("/push-subscription");
        Roles(this.GetUserRole());
        Summary(s => s.Summary = "Register a Web Push subscription for the current user");
    }

    public override async Task HandleAsync(SubscribePushRequest req, CancellationToken ct)
    {
        if (!await IsEndpointAllowedAsync(req.Endpoint, ct))
        {
            AddError(r => r.Endpoint, "Push endpoint must use HTTPS and must not target a private or loopback address.");
            ThrowIfAnyErrors();
        }

        var userId = User.GetId();

        var existing = await dbContext.Set<PushSubscription>()
            .FirstOrDefaultAsync(x => x.Endpoint == req.Endpoint, ct);

        if (existing is not null)
            await ApplyUpdateAsync(existing, userId, req, ct);
        else
            await dbContext.Set<PushSubscription>().AddAsync(new PushSubscription
            {
                UserId = userId,
                Endpoint = req.Endpoint,
                P256dh = req.P256dh,
                Auth = req.Auth,
                UserAgent = req.UserAgent
            }, ct);

        try
        {
            await dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Concurrent first-subscribe for the same endpoint — update the now-existing row.
            dbContext.ChangeTracker.Clear();
            var row = await dbContext.Set<PushSubscription>().FirstAsync(x => x.Endpoint == req.Endpoint, ct);
            await ApplyUpdateAsync(row, userId, req, ct);
            await dbContext.SaveChangesAsync(ct);
        }

        await Send.NoContentAsync(ct);
    }

    private async Task ApplyUpdateAsync(PushSubscription sub, long userId, SubscribePushRequest req, CancellationToken ct)
    {
        if (sub.UserId != userId)
        {
            // Re-ownership: a different user is claiming this device's endpoint.
            // Audit the transfer and replace the row so no stale keys from the previous owner remain.
            await auditService.LogAsync("PushSubscriptionReowned", new
            {
                PreviousUserId = sub.UserId,
                NewUserId = userId,
                req.Endpoint
            }, nameof(PushSubscription), sub.Id, ct);

            dbContext.Set<PushSubscription>().Remove(sub);
            await dbContext.Set<PushSubscription>().AddAsync(new PushSubscription
            {
                UserId = userId,
                Endpoint = req.Endpoint,
                P256dh = req.P256dh,
                Auth = req.Auth,
                UserAgent = req.UserAgent
            }, ct);
            return;
        }

        sub.P256dh = req.P256dh;
        sub.Auth = req.Auth;
        sub.UserAgent = req.UserAgent;

        // Force-touch: the SPA re-POSTs the same subscription on every app start, and an identical
        // re-POST changes no property — EF would leave the entry Unchanged and ModifiedTimestamp would
        // freeze at the row's creation. The retention purge treats ModifiedTimestamp as the device's
        // last-seen instant (PurgeExpiredNotificationHistoryJobHandler.SubscriptionStaleDays), so
        // without this a perfectly healthy device would age out. Marking the column modified puts the
        // entry in Modified state, which BaseDbContext's save override then stamps with UtcNow.
        dbContext.Entry(sub).Property(x => x.ModifiedTimestamp).IsModified = true;
    }

    private static async Task<bool> IsEndpointAllowedAsync(string endpoint, CancellationToken ct)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
            return false;
        if (uri.Scheme != Uri.UriSchemeHttps)
            return false;

        // Literal IP in the host: validate it directly, no DNS lookup needed.
        if (IPAddress.TryParse(uri.Host, out var literalIp))
            return !IsPrivateOrLocal(literalIp);

        // Hostname: resolve it and reject if ANY resolved address is private/loopback (SEC-2).
        // A host that fails to resolve cannot reach an internal target, so it is allowed — this
        // also keeps reserved test domains (*.example) working without depending on live DNS.
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(uri.Host, ct);
            return !addresses.Any(IsPrivateOrLocal);
        }
        catch (SocketException)
        {
            return true;
        }
    }

    private static bool IsPrivateOrLocal(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip))
            return true;

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            return b[0] == 10 // 10.0.0.0/8
                   || (b[0] == 172 && b[1] is >= 16 and <= 31) // 172.16.0.0/12
                   || (b[0] == 192 && b[1] == 168) // 192.168.0.0/16
                   || (b[0] == 169 && b[1] == 254); // 169.254.0.0/16 link-local
        }

        return false;
    }
}