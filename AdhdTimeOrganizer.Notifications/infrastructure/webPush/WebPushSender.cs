using System.Net;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Microsoft.Extensions.Options;
using Sydowwe.Framework.config.dependencyInjection;
using LibPushSubscription = Lib.Net.Http.WebPush.PushSubscription;

namespace AdhdTimeOrganizer.Notifications.infrastructure.webPush;

public class WebPushSender(IHttpClientFactory httpClientFactory, IOptions<PushNotificationOptions> options)
    : IWebPushSender, IScopedService
{
    public async Task<bool> SendAsync(PushTarget target, string payloadJson, CancellationToken ct = default)
    {
        var o = options.Value;

        var client = new PushServiceClient(httpClientFactory.CreateClient(nameof(WebPushSender)))
        {
            DefaultAuthentication = new VapidAuthentication(o.VapidPublicKey, o.VapidPrivateKey)
            {
                Subject = o.VapidSubject
            }
        };

        var subscription = new LibPushSubscription { Endpoint = target.Endpoint };
        subscription.SetKey(PushEncryptionKeyName.P256DH, target.P256dh);
        subscription.SetKey(PushEncryptionKeyName.Auth, target.Auth);

        var message = new PushMessage(payloadJson);

        try
        {
            await client.RequestPushMessageDeliveryAsync(subscription, message, ct);
            return false;
        }
        catch (PushServiceClientException ex) when (
            ex.StatusCode is HttpStatusCode.Gone or HttpStatusCode.NotFound)
        {
            // Subscription no longer valid — signal the caller to prune it.
            return true;
        }
    }
}