using AdhdTimeOrganizer.History.application.notification;
using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sydowwe.Framework.Contracts.notification;
using Sydowwe.Framework.Contracts.notification.payload;
using Sydowwe.Framework.Testing;
using Sydowwe.Notifications.application.dto;
using Sydowwe.Notifications.domain.entity;
using Sydowwe.Notifications.infrastructure;
using Sydowwe.Notifications.infrastructure.email;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// Frontend-queue §S3 and §S4 (<c>notifications/B4</c> and <c>P5</c>), pinned on rows rather than status codes —
/// each of these failures is a perfectly valid 200/204.
/// <list type="bullet">
/// <item><b>§S3</b> — the dispatcher counted a recipient's stored <i>preference</i> for every channel, never asking
/// whether the deployment can deliver on it. Web Push is default-on for every type, so on a host with no VAPID keys
/// unticking In-app still left a channel "enabled", and the history row was written anyway while the settings
/// screen told the user "not even in the notification list".</item>
/// <item><b>§S4</b> — <see cref="NotificationType.Test"/> is a dev/QA probe, yet the catalog offered it to every
/// account, and <c>POST /notification/test</c> let any account raise it.</item>
/// </list>
/// </summary>
[Collection("Postgres")]
public class NotificationDeliverableChannelsTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    /// <summary>SMTP off explicitly (the fixture sets every MAIL_* var), no VAPID keys: only In-app can deliver.</summary>
    private static readonly KeyValuePair<string, string?>[] InAppOnly =
    [
        new($"{EmailNotificationOptions.SectionName}:Enabled", "false")
    ];

    private static readonly KeyValuePair<string, string?>[] PushConfigured =
    [
        new($"{EmailNotificationOptions.SectionName}:Enabled", "false"),
        new($"{PushNotificationOptions.SectionName}:VapidSubject", "mailto:test@example.com"),
        new($"{PushNotificationOptions.SectionName}:VapidPublicKey", "test-public-key"),
        new($"{PushNotificationOptions.SectionName}:VapidPrivateKey", "test-private-key")
    ];

    // ---- §S3 --------------------------------------------------------------------------------------

    [Fact(DisplayName = "§S3: a type muted in-app writes no history row when nothing else can deliver")]
    public async Task MutedInApp_WithPushAndEmailUnconfigured_WritesNoRow()
    {
        await using var factory = CreateFactory(TestRoles.AdminAndUser, configOverrides: InAppOnly);
        AssertOnlyInAppCanDeliver(factory);
        await SetPreferenceAsync(NotificationType.DeadlineApproaching, NotificationChannel.InApp, false);
        var before = await CountRowsAsync(NotificationType.DeadlineApproaching);

        await NotifyAsync(factory, NotificationType.DeadlineApproaching);

        (await CountRowsAsync(NotificationType.DeadlineApproaching)).Should().Be(before,
            "Push and E-mail default to on, but neither can reach anyone here — so the user has silenced everything "
            + "that can deliver, and the screen says exactly that");
    }

    [Fact(DisplayName = "§S3: control — the same type with In-app left on still lands in the inbox")]
    public async Task InAppOn_WithPushAndEmailUnconfigured_WritesTheRow()
    {
        await using var factory = CreateFactory(TestRoles.AdminAndUser, configOverrides: InAppOnly);
        AssertOnlyInAppCanDeliver(factory);
        var before = await CountRowsAsync(NotificationType.ReminderDigest);

        await NotifyAsync(factory, NotificationType.ReminderDigest);

        (await CountRowsAsync(NotificationType.ReminderDigest)).Should().Be(before + 1,
            "discounting unconfigured channels must not swallow a notification the user did not mute");
    }

    [Fact(DisplayName = "§S3: control — a muted In-app box still keeps the row while Push can deliver")]
    public async Task MutedInApp_WithPushConfigured_StillWritesTheRow()
    {
        await using var factory = CreateFactory(TestRoles.AdminAndUser, configOverrides: PushConfigured);
        factory.Services.GetRequiredService<IOptions<PushNotificationOptions>>().Value.IsConfigured.Should().BeTrue();
        await SetPreferenceAsync(HistoryNotificationTypes.TimerBoundary, NotificationChannel.InApp, false);
        var before = await CountRowsAsync(HistoryNotificationTypes.TimerBoundary);

        await NotifyAsync(factory, HistoryNotificationTypes.TimerBoundary);

        (await CountRowsAsync(HistoryNotificationTypes.TimerBoundary)).Should().Be(before + 1,
            "a deliverable background channel is still a way to reach the user, and the row is its history");
    }

    // ---- §S4 --------------------------------------------------------------------------------------

    [Fact(DisplayName = "§S4: an ordinary account's preference matrix has no Test rows")]
    public async Task Matrix_ForAUser_OmitsTheTestKind()
    {
        var items = await GetMatrixAsync(CreateUserRoleClient());

        items.Select(i => i.Type).Should().NotContain(NotificationType.Test);
        items.Should().NotBeEmpty("the rest of the catalog is still offered");
        items.Select(i => i.Type).Should().Contain(NotificationType.PersonalReminder);
    }

    [Fact(DisplayName = "§S4: an administrator's matrix still offers Test, on every channel")]
    public async Task Matrix_ForAnAdmin_KeepsTheTestKind()
    {
        var items = await GetMatrixAsync(CreateAdminRoleClient());

        items.Where(i => i.Type == NotificationType.Test).Select(i => i.Channel)
            .Should().BeEquivalentTo(Enum.GetValues<NotificationChannel>());
    }

    [Fact(DisplayName = "§S4: an ordinary account cannot write a preference for Test, and nothing is stored")]
    public async Task Update_ForAUser_RejectsTheTestKind()
    {
        var response = await CreateUserRoleClient().PutAsJsonAsync("api/notification-preference",
            new { type = nameof(NotificationType.Test), channel = nameof(NotificationChannel.InApp), enabled = false },
            JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await using var db = CreateDbContext();
        (await db.Set<NotificationPreference>().AsNoTracking().AnyAsync(p => p.Type == NotificationType.Test, CancellationToken))
            .Should().BeFalse("a row for a kind the account's matrix can never show is invisible and permanent");
    }

    [Fact(DisplayName = "§S4: an administrator can still write a preference for Test")]
    public async Task Update_ForAnAdmin_AcceptsTheTestKind()
    {
        var response = await CreateAdminRoleClient().PutAsJsonAsync("api/notification-preference",
            new { type = nameof(NotificationType.Test), channel = nameof(NotificationChannel.InApp), enabled = false },
            JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact(DisplayName = "§S4: only an administrator can raise a Test notification")]
    public async Task SendTest_IsAdminOnly()
    {
        (await CreateUserRoleClient().PostAsync("api/notification/test", null, CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await CreateAdminRoleClient().PostAsync("api/notification/test", null, CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ---- helpers ----------------------------------------------------------------------------------

    private static void AssertOnlyInAppCanDeliver(ITestClientFactory factory)
    {
        factory.Services.GetRequiredService<IOptions<PushNotificationOptions>>().Value.IsConfigured
            .Should().BeFalse("the premise: this host has no VAPID keys");
        factory.Services.GetRequiredService<IOptions<EmailNotificationOptions>>().Value.IsConfigured
            .Should().BeFalse("the premise: SMTP is off");
    }

    private async Task NotifyAsync(ITestClientFactory factory, NotificationType type)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<INotificationService>().NotifyAsync(
            NotificationRecipients.User(UserId), type, SamplePayload(type), ct: CancellationToken);
    }

    private static INotificationPayload? SamplePayload(NotificationType type) => type.Key switch
    {
        nameof(NotificationType.DeadlineApproaching) => new DeadlineApproachingPayload("Feature Development"),
        nameof(NotificationType.ReminderDigest) => new ReminderDigestPayload(3),
        _ => null
    };

    private async Task SetPreferenceAsync(NotificationType type, NotificationChannel channel, bool enabled)
    {
        await using var db = CreateDbContext();
        db.Add(new NotificationPreference { UserId = UserId, Type = type, Channel = channel, Enabled = enabled });
        await db.SaveChangesAsync(CancellationToken);
    }

    private async Task<int> CountRowsAsync(NotificationType type)
    {
        await using var db = CreateDbContext();
        return await db.Set<Notification>().AsNoTracking().CountAsync(n => n.UserId == UserId && n.Type == type, CancellationToken);
    }

    private async Task<List<NotificationPreferenceItem>> GetMatrixAsync(HttpClient client)
    {
        var items = await client.GetFromJsonAsync<List<NotificationPreferenceItem>>(
            "api/notification-preference/mine", JsonOpts, CancellationToken);
        items.Should().NotBeNull();
        return items!;
    }
}
