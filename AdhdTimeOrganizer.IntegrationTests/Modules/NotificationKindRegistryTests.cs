using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sydowwe.Framework.Contracts.notification;
using Sydowwe.Framework.Contracts.notification.kind;
using Sydowwe.Framework.Contracts.notification.payload;
using Sydowwe.Framework.Contracts.reminders;
using Sydowwe.Framework.Testing;
using Sydowwe.Reminders.domain.entity;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Modules;

/// <summary>
/// The notification kind registry refuses a broken composition <b>at startup</b>. Both shapes below compile, and
/// without the check both fail somewhere far from their cause: two kinds on one key means one of them silently
/// renders the other's words, and a payload record naming another kind means the typed <c>NotifyAsync</c> files
/// every notification under the wrong key — wrong words, wrong settings row, wrong buttons.
/// <para>
/// Asserted on the host failing to start, which is also what pins the startup check's registration in
/// <c>Program.cs</c>: drop that line and these hosts boot, and the first notification throws instead.
/// </para>
/// </summary>
[Collection("Postgres")]
public class NotificationKindRegistryTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    [Fact(DisplayName = "A host with two kinds on one key does not start")]
    public async Task DuplicateKey_FailsHostStartup()
    {
        var failure = await StartupFailureAsync(s => s.AddSingleton<INotificationKind, SecondTestKind>());

        failure.Should().Contain("Two notification kinds declare the key 'Test'")
            .And.Contain(nameof(SecondTestKind), "the message names the newcomer, so the fix is obvious");
    }

    [Fact(DisplayName = "A host whose kind is raised with another kind's payload record does not start")]
    public async Task PayloadAttributeMismatch_FailsHostStartup()
    {
        var failure = await StartupFailureAsync(s => s.AddSingleton<INotificationKind, BorrowedPayloadKind>());

        failure.Should().Contain("'MismatchProbe'").And.Contain("names 'Test'");
    }

    /// <summary>
    /// Every kind this host raises has a definition in the composition. A slice whose assembly dropped out of
    /// <c>ModuleServiceExtensions.ModuleAssemblies</c> takes its kinds with it, and nothing else notices: they render the
    /// generic text, lose their policy (an exempt alarm gets deferred, a held nudge gets pushed) and their buttons.
    /// </summary>
    [Fact(DisplayName = "Every catalogued kind resolves to a registered definition, from the slice that raises it")]
    public void EveryCataloguedKind_IsRegistered()
    {
        var registry = Fixture.AdminAndUserFactory.Services.GetRequiredService<Sydowwe.Notifications.application.kind.INotificationKindRegistry>();

        var unregistered = Endpoints.NotificationCatalogTests.Catalog.Where(t => registry.Find(t) is null).ToList();
        unregistered.Should().BeEmpty("a catalogued kind with no definition renders generically and has no policy of its own");

        // And from the assembly that raises it, not some leftover copy: a kind's owner is its definition's assembly.
        registry.Kinds.Where(k => Endpoints.NotificationCatalogTests.Catalog.Contains(k.Type))
            .Select(k => k.GetType().Assembly.GetName().Name)
            .Distinct()
            .Should().BeEquivalentTo(
                "AdhdTimeOrganizer", "AdhdTimeOrganizer.History", "AdhdTimeOrganizer.Routines", "AdhdTimeOrganizer.Planning",
                "AdhdTimeOrganizer.TodoLists", "AdhdTimeOrganizer.Tracking", "Sydowwe.Notifications");
    }

    /// <summary>
    /// A kind is an open string key, so a typo is not a compile error, and the admin register route binds any string.
    /// A reminder registered under a key no kind defines would be stored and then delivered, for its whole life, with
    /// the generic text and no settings row — so the registry refuses it, and leaves no definition behind.
    /// </summary>
    [Fact(DisplayName = "A reminder cannot be registered under a notification kind nothing defines")]
    public async Task ReminderRegistration_RefusesAnUnregisteredKind()
    {
        await using var scope = Fixture.AdminAndUserFactory.Services.CreateAsyncScope();
        var registry = scope.ServiceProvider.GetRequiredService<IReminderRegistry>();

        var register = () => registry.RegisterAsync(new ReminderRegistration
        {
            Key = new ReminderKey("R1Probe", "Probe", "1", "Probe"),
            Schedule = ReminderSchedule.OneShot(DateTimeOffset.UtcNow.AddDays(1), [0]),
            TemplateKey = "r1.probe",
            NotificationType = new NotificationType("PersonalReminderr"),
            RecipientMode = RecipientMode.ExplicitUsers,
            ExplicitRecipientUserIds = [FakeLoggedUserService.TestUserId]
        }, CancellationToken);

        (await register.Should().ThrowAsync<ArgumentException>()).WithMessage("*'PersonalReminderr'*");

        await using var db = CreateDbContext();
        (await db.Set<ReminderDefinition>().AnyAsync(d => d.OwnerModule == "R1Probe", CancellationToken))
            .Should().BeFalse("a refused registration stores nothing");
    }

    /// <summary>Boots a host with the extra registration and returns every message in the exception chain it died with.</summary>
    private async Task<string> StartupFailureAsync(Action<IServiceCollection> configureServices)
    {
        var factory = CreateFactory(TestRoles.AdminAndUser, configureServices: configureServices);
        try
        {
            var act = () => factory.Services;
            var thrown = act.Should().Throw<Exception>("the startup check builds the registry while the host starts").Which;
            return string.Join(" | ", Chain(thrown).Select(e => e.Message));
        }
        finally
        {
            try
            {
                await factory.DisposeAsync();
            }
            catch
            {
                // A host that never started can fail to stop; that is not what is under test.
            }
        }
    }

    private static IEnumerable<Exception> Chain(Exception e)
    {
        for (var current = e; current is not null; current = current.InnerException)
        {
            yield return current;
            if (current is AggregateException aggregate)
                foreach (var inner in aggregate.InnerExceptions.SelectMany(Chain))
                    yield return inner;
        }
    }

    /// <summary>A second kind claiming the framework's <c>Test</c> key, with the right payload record.</summary>
    private sealed class SecondTestKind : NotificationKind<TestNotificationPayload>
    {
        public override NotificationType Type => FrameworkNotificationTypes.Test;

        protected override (string Title, string Body) Render(TestNotificationPayload? payload, NotificationRenderContext context) => ("x", "x");
    }

    /// <summary>A kind of its own, raised with a record whose attribute names <c>Test</c>.</summary>
    private sealed class BorrowedPayloadKind : NotificationKind<TestNotificationPayload>
    {
        public override NotificationType Type => new("MismatchProbe");

        protected override (string Title, string Body) Render(TestNotificationPayload? payload, NotificationRenderContext context) => ("x", "x");
    }
}
