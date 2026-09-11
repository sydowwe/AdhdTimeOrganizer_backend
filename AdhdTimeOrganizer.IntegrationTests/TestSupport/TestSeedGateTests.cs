using AdhdTimeOrganizer.infrastructure.testSupport;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.TestSupport;

/// <summary>
/// The gate on <c>POST /api/test/seed</c>, tested on its own because it is the whole security control:
/// the endpoint truncates the database and requires no authentication, so "may this process expose it"
/// is the only question standing between a test run and a production wipe.
///
/// <para>These are pure decision tests — no host, no database. The half that cannot be asserted here,
/// "a closed gate means the route is not registered", is in
/// <see cref="TestSeedRouteRegistrationTests"/>.</para>
/// </summary>
public class TestSeedGateTests
{
    private const string LocalConnection = "Host=localhost;Port=5432;Database=database;Username=test;Password=test";

    /// <summary>The shape this solution actually deploys — see <c>AdhdTimeOrganizer/.env</c>.</summary>
    private const string SharedRemoteConnection = "Host=187.77.77.42;Port=5443;Database=main_db;Username=api;Password=secret";

    private static IConfiguration Config(params (string Key, string Value)[] entries) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(entries.Select(e => new KeyValuePair<string, string?>(e.Key, e.Value)))
            .Build();

    [Fact]
    public void Development_AgainstALocalDatabase_Opens()
    {
        var decision = TestSeedGate.Evaluate("Development", Config(), LocalConnection);

        decision.IsOpen.Should().BeTrue(decision.Reason);
    }

    // --- the database half: on this machine, or not at all ----------------------------------------

    [Theory]
    [InlineData("localhost")]
    [InlineData("LOCALHOST")]
    [InlineData("127.0.0.1")]
    // The whole of 127.0.0.0/8 is loopback, which is why this is IPAddress.IsLoopback and not a
    // string compare against "127.0.0.1".
    [InlineData("127.0.0.53")]
    [InlineData("::1")]
    [InlineData("[::1]")]
    // A containerised app reaching a Postgres on the developer's own host. Still a local database.
    [InlineData("host.docker.internal")]
    public void ALocalHost_Opens(string host)
    {
        var decision = TestSeedGate.Evaluate("Development", Config(), $"Host={host};Database=anything;Username=u;Password=p");

        decision.IsOpen.Should().BeTrue(decision.Reason);
    }

    /// <summary>
    /// The case this project is actually exposed to. Dev and prod here are both <c>main_db</c> on the
    /// same remote host and differ only by port, so neither the database name nor the port could have
    /// separated them — being off this machine is what refuses both.
    /// </summary>
    [Fact]
    public void TheSharedRemoteDatabase_StaysClosed_OnBothPorts()
    {
        var dev = TestSeedGate.Evaluate("Development", Config(), SharedRemoteConnection);
        var prod = TestSeedGate.Evaluate("Development", Config(), SharedRemoteConnection.Replace("Port=5443", "Port=5444"));

        dev.IsOpen.Should().BeFalse();
        prod.IsOpen.Should().BeFalse();
        dev.Reason.Should().Contain("not this machine");
    }

    /// <summary>
    /// There is no configuration key that admits a remote database, and there must not be: any such
    /// list is a way to point a truncating endpoint at a shared database. This pins that no plausible
    /// spelling of one has quietly appeared.
    /// </summary>
    [Theory]
    [InlineData("TestSeed:AllowedDatabases:0", "187.77.77.42:5443/main_db")]
    [InlineData("TestSeed:AllowedDatabases:0", "*")]
    [InlineData("TestSeed:AllowedHosts:0", "187.77.77.42")]
    [InlineData("TestSeed:AllowedPorts:0", "5443")]
    [InlineData("TestSeed:AllowRemoteDatabase", "true")]
    public void NoConfiguration_CanAdmitARemoteDatabase(string key, string value)
    {
        TestSeedGate.Evaluate("Development", Config((key, value)), SharedRemoteConnection)
            .IsOpen.Should().BeFalse();
    }

    /// <summary>
    /// A hostname that is not a known local name and not a loopback literal is refused rather than
    /// resolved. A gate whose answer depends on a DNS lookup changes without the connection string
    /// changing.
    /// </summary>
    [Theory]
    [InlineData("db.internal")]
    [InlineData("localhost.evil.example.com")]
    [InlineData("my-localhost")]
    [InlineData("10.0.0.5")]
    [InlineData("0.0.0.0")]
    public void AHostThatIsNotDemonstrablyLocal_StaysClosed(string host)
    {
        TestSeedGate.Evaluate("Development", Config(), $"Host={host};Database=database;Username=u;Password=p")
            .IsOpen.Should().BeFalse();
    }

    // --- the environment half ---------------------------------------------------------------------

    /// <summary>
    /// <c>TestSeed:AllowedEnvironments</c> is a widening knob, and this is the part it must not be able
    /// to widen onto. Both entries below would open the gate if the list were consulted first.
    /// </summary>
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void ProductionAndStaging_StayClosed_EvenWhenAllowListed(string environment)
    {
        var config = Config(("TestSeed:AllowedEnvironments:0", environment));

        var decision = TestSeedGate.Evaluate(environment, config, LocalConnection);

        decision.IsOpen.Should().BeFalse();
        decision.Reason.Should().Contain("can never expose it");
    }

    [Fact]
    public void AnUnlistedEnvironment_StaysClosed()
    {
        TestSeedGate.Evaluate("QA", Config(), LocalConnection).IsOpen.Should().BeFalse();

        TestSeedGate.Evaluate("QA", Config(("TestSeed:AllowedEnvironments:0", "QA")), LocalConnection)
            .IsOpen.Should().BeTrue();
    }

    /// <summary>Both halves are required — neither one on its own opens anything.</summary>
    [Fact]
    public void BothHalvesAreRequired()
    {
        // Right environment, wrong machine.
        TestSeedGate.Evaluate("Development", Config(), SharedRemoteConnection).IsOpen.Should().BeFalse();

        // Right machine, wrong environment.
        TestSeedGate.Evaluate("Production", Config(), LocalConnection).IsOpen.Should().BeFalse();
    }

    // --- fails closed ------------------------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AnUndeterminableEnvironment_StaysClosed(string? environment)
    {
        var decision = TestSeedGate.Evaluate(environment, Config(), LocalConnection);

        decision.IsOpen.Should().BeFalse();
        decision.Reason.Should().Contain("could not be determined");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Host=;Database=;")]
    [InlineData("this is not a connection string")]
    public void AnUnusableConnectionString_StaysClosed(string? connectionString)
    {
        TestSeedGate.Evaluate("Development", Config(), connectionString).IsOpen.Should().BeFalse();
    }

    /// <summary>The reason is logged, so it must never carry the credentials or the database name.</summary>
    [Fact]
    public void TheReason_NeverCarriesTheCredentialsOrTheDatabaseName()
    {
        var closed = TestSeedGate.Evaluate("Development", Config(), SharedRemoteConnection);
        var open = TestSeedGate.Evaluate("Development", Config(), LocalConnection);

        foreach (var reason in new[] { closed.Reason, open.Reason })
            reason.Should().NotContain("secret").And.NotContain("Password").And.NotContain("main_db");
    }
}
