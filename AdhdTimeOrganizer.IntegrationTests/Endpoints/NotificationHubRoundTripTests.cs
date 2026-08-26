using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Sydowwe.Framework.Contracts.notification;
using Sydowwe.Framework.Contracts.notification.payload;
using Sydowwe.Framework.Testing;
using Sydowwe.Notifications.application.dto;
using Sydowwe.Notifications.infrastructure.realtime;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

[Collection("Postgres")]
public class NotificationHubRoundTripTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    [Fact(DisplayName = "An authenticated WebSocket client receives only its own live notification")]
    public async Task NotificationHub_DeliversTheDispatcherMessageOverWebSocket()
    {
        await using var factory = new KestrelTestFactory(ConnectionString, TestRoles.AdminAndUser);
        using var bootstrapClient = factory.CreateClient();
        var address = factory.Services.GetRequiredService<IServer>().Features
            .Get<IServerAddressesFeature>()!.Addresses.Single();

        await using var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(new Uri(address), "/hubs/notifications"), options =>
            {
                options.Transports = HttpTransportType.WebSockets;
                options.SkipNegotiation = true;
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
        notification.Type.Should().Be(NotificationType.Test);
        notification.Body.Should().Be("WebSocket round trip");
    }

    private sealed class KestrelTestFactory(string connectionString, string[] roles)
        : TestWebApplicationFactory<Program>(connectionString, roles)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseKestrel(options => options.ListenLocalhost(0));
        }
    }
}
