using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Sydowwe.Framework.Contracts.notification;
using Sydowwe.Framework.Contracts.notification.kind;
using Sydowwe.Framework.Contracts.notification.payload;
using Sydowwe.Framework.Testing;
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

    [Fact(DisplayName = "The real composition starts, and its registry resolves")]
    public void RealComposition_Starts() =>
        Fixture.AdminAndUserFactory.Services.GetRequiredService<Sydowwe.Notifications.application.kind.INotificationKindRegistry>()
            .Kinds.Should().NotBeEmpty();

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
