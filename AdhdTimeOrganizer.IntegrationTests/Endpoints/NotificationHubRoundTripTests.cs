using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Sydowwe.Framework.Contracts.notification;
using Sydowwe.Framework.Contracts.notification.kind;
using Sydowwe.Framework.Contracts.notification.payload;
using Sydowwe.Framework.Testing;
using Sydowwe.Notifications.application.dto;
using Sydowwe.Notifications.infrastructure.realtime;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

[Collection("Postgres")]
public class NotificationHubRoundTripTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    /// <summary>
    /// Drives the hub through <c>TestServer</c>'s own WebSocket client rather than a real socket on a
    /// real port.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not a stylistic choice — <c>WebApplicationFactory</c> applies <c>UseTestServer()</c> after
    /// <c>ConfigureWebHost</c> runs, so a <c>UseKestrel</c> there is overwritten and the host that comes
    /// back is a <c>TestServer</c> whose <c>IServerAddressesFeature</c> is empty. Asking it for a listen
    /// address throws, which is exactly how this test failed when it was written that way.
    /// </para>
    /// <para>
    /// Standing up a second, real Kestrel host next to the test one would fix the address but break the
    /// assertion: SignalR's default backplane is per-host and in-memory, so a notification dispatched
    /// through this factory's services would never reach a client connected to the other host. One host
    /// is what makes the round trip a round trip.
    /// </para>
    /// <para>
    /// <c>CreateWebSocketClient</c> still performs a genuine WebSocket handshake and frames every
    /// message, so the transport under test is the real one; only the socket's transport is in-memory.
    /// </para>
    /// </remarks>
    [Fact(DisplayName = "An authenticated WebSocket client receives only its own live notification")]
    public async Task NotificationHub_DeliversTheDispatcherMessageOverWebSocket()
    {
        await using var factory = new TestWebApplicationFactory<Program>(ConnectionString, TestRoles.AdminAndUser);
        using var bootstrapClient = factory.CreateClient();
        var server = factory.Server;

        await using var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, "/hubs/notifications"), options =>
            {
                options.Transports = HttpTransportType.WebSockets;
                // Negotiation would go out over a plain HttpClient that knows nothing about the in-memory
                // server; skipping it sends the client straight at the WebSocket factory below.
                options.SkipNegotiation = true;
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                options.WebSocketFactory = async (context, ct) =>
                {
                    // The client hands us a ws:// URI; TestServer's client speaks http:// and upgrades.
                    var handshakeUri = new UriBuilder(context.Uri) { Scheme = Uri.UriSchemeHttp }.Uri;
                    return await server.CreateWebSocketClient().ConnectAsync(handshakeUri, ct);
                };
            })
            .Build();

        var received = new TaskCompletionSource<NotificationDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<NotificationDto>(NotificationHub.ReceiveMethod, notification =>
        {
            received.TrySetResult(notification);
        });

        await connection.StartAsync(CancellationToken);

        await using (var scope = factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<INotificationService>().NotifyAsync(
                NotificationRecipients.User(FakeLoggedUserService.TestUserId),
                new TestNotificationPayload("WebSocket round trip"),
                CancellationToken);

        var notification = await received.Task.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken);
        notification.Type.Should().Be(FrameworkNotificationTypes.Test);
        notification.Body.Should().Be("WebSocket round trip");
    }
}
