using AdhdTimeOrganizer.Notifications.application.dto;
using AdhdTimeOrganizer.Notifications.infrastructure;
using FastEndpoints;
using Microsoft.Extensions.Options;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.Notifications.application.endpoint.pushSubscription;

/// <summary>
/// Hands the VAPID application server key to the SPA so it can call <c>PushManager.subscribe()</c>.
/// The public half is not a secret — it ships to every browser that subscribes — so returning it is
/// by design; the private key never leaves the server. When the deployment has no VAPID credentials
/// the response carries a null key rather than 404/500, so the client can quietly skip push setup.
/// </summary>
public class GetVapidPublicKeyEndpoint(IOptions<PushNotificationOptions> options)
    : EndpointWithoutRequest<VapidPublicKeyResponse>
{
    public override void Configure()
    {
        Get("/push-subscription/vapid-public-key");
        Roles(this.GetUserRole());
        Summary(s => s.Summary = "Get the VAPID public key for Web Push subscription");
    }

    public override Task HandleAsync(CancellationToken ct)
    {
        var o = options.Value;
        var key = o.IsConfigured ? o.VapidPublicKey : null;

        return Send.OkAsync(new VapidPublicKeyResponse(key), ct);
    }
}