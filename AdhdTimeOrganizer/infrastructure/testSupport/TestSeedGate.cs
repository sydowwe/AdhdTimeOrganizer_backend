using System.Net;
using Npgsql;

namespace AdhdTimeOrganizer.infrastructure.testSupport;

/// <summary>
/// Whether <c>POST /api/test/seed</c> exists in this process at all, and why.
/// </summary>
/// <param name="IsOpen">True only when every condition in <see cref="TestSeedGate"/> passed.</param>
/// <param name="Reason">
/// One sentence, safe to log. Never returned over HTTP — outside the gate the route is not registered,
/// so there is no response to carry it. It is written to the log once at boot instead.
/// </param>
public sealed record TestSeedGateDecision(bool IsOpen, string Reason);

/// <summary>
/// The gate on the destructive test-seed endpoint. It answers one question at startup — may this
/// process expose a route that truncates the database? — and the answer decides whether the endpoint
/// is <b>registered</b>, not whether it is authorized.
///
/// <para><b>Two independent conditions, both required.</b></para>
/// <list type="number">
/// <item>
/// The hosting environment is on the allow-list: <c>Development</c> always, plus anything a deployment
/// adds through <c>TestSeed:AllowedEnvironments</c>. <c>Production</c> and <c>Staging</c> are refused
/// unconditionally — adding either to that list does nothing.
/// </item>
/// <item>
/// <b>The database is on this machine.</b> The connection string's host must be loopback
/// (<c>localhost</c>, anything in <c>127.0.0.0/8</c>, <c>::1</c>) or <c>host.docker.internal</c>, which
/// is how a containerised app reaches a Postgres on its own host.
/// </item>
/// </list>
///
/// <para><b>The second condition takes no configuration, deliberately.</b> There is no allow-list to
/// add a remote database to, because any such list is a way to point this endpoint at a shared
/// database and the whole value of the check is that it cannot be. Run e2e against a local Postgres.</para>
///
/// <para><b>Why the port and the database name are not consulted.</b> Neither can separate this
/// solution's databases: dev and prod are both <c>main_db</c> on the same remote host and differ only
/// by port (see <c>AdhdTimeOrganizer/.env</c>). A name check is therefore worthless, and a port check
/// would admit <i>any</i> host that happened to serve on the allowed port — including a production one.
/// "Is it on this machine" is the only question with an answer that cannot be got wrong by editing a
/// connection string.</para>
///
/// <para><b>Fails closed.</b> An environment name that is missing, a connection string that will not
/// parse, a host that is absent or does not resolve — every one of them closes the gate. There is no
/// path through this class that opens it by omission.</para>
/// </summary>
public static class TestSeedGate
{
    public const string SectionName = "TestSeed";

    /// <summary>Always allowed, without configuration.</summary>
    private const string DevelopmentEnvironment = "Development";

    /// <summary>
    /// Refused even when named in <c>TestSeed:AllowedEnvironments</c>. The list is a widening knob, and
    /// this is the part of the gate it must not be able to widen onto.
    /// </summary>
    private static readonly string[] NeverAllowedEnvironments = ["Production", "Staging"];

    /// <summary>
    /// Hostnames that mean "this machine" but are not IP literals, so <see cref="IPAddress.IsLoopback"/>
    /// cannot recognise them. <c>host.docker.internal</c> is here because the app may run in a container
    /// against a Postgres on the developer's own host — that is still a local database.
    /// </summary>
    private static readonly string[] LocalHostNames = ["localhost", "host.docker.internal"];

    public static TestSeedGateDecision Evaluate(string? environmentName, IConfiguration configuration, string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(environmentName))
            return new TestSeedGateDecision(false, "the hosting environment could not be determined");

        if (NeverAllowedEnvironments.Contains(environmentName, StringComparer.OrdinalIgnoreCase))
            return new TestSeedGateDecision(false, $"the '{environmentName}' environment can never expose it");

        var allowedEnvironments = configuration.GetSection($"{SectionName}:AllowedEnvironments").Get<string[]>() ?? [];
        if (!environmentName.Equals(DevelopmentEnvironment, StringComparison.OrdinalIgnoreCase)
            && !allowedEnvironments.Contains(environmentName, StringComparer.OrdinalIgnoreCase))
            return new TestSeedGateDecision(false,
                $"the '{environmentName}' environment is not in {SectionName}:AllowedEnvironments");

        if (string.IsNullOrWhiteSpace(connectionString))
            return new TestSeedGateDecision(false, "no database connection string is configured");

        NpgsqlConnectionStringBuilder csb;
        try
        {
            csb = new NpgsqlConnectionStringBuilder(connectionString);
        }
        catch (Exception)
        {
            // The message would carry the password, so it is deliberately not included.
            return new TestSeedGateDecision(false, "the database connection string could not be parsed");
        }

        var host = csb.Host;
        if (string.IsNullOrWhiteSpace(host))
            return new TestSeedGateDecision(false, "the connection string names no database host");

        // Safe to log: the host only. Never the database name, the user or the password.
        return IsLocal(host)
            ? new TestSeedGateDecision(true, $"environment '{environmentName}', database host '{host}'")
            : new TestSeedGateDecision(false, $"the database host '{host}' is not this machine");
    }

    /// <summary>
    /// "Is this database on the machine the app is running on." Named hosts first, then the IP forms —
    /// <see cref="IPAddress.IsLoopback"/> rather than a string compare against <c>127.0.0.1</c>, so the
    /// whole of <c>127.0.0.0/8</c> and <c>::1</c> are covered without listing them.
    /// </summary>
    /// <remarks>
    /// Deliberately does <b>not</b> resolve DNS. A name that resolves to a loopback address today is a
    /// name that can resolve elsewhere tomorrow, and a gate whose answer depends on a DNS lookup is a
    /// gate that changes without the connection string changing. A host that is neither a known local
    /// name nor a loopback literal is refused, which is the fail-closed direction.
    /// </remarks>
    private static bool IsLocal(string host)
    {
        if (LocalHostNames.Contains(host, StringComparer.OrdinalIgnoreCase))
            return true;

        // Npgsql accepts a bracketed IPv6 literal; IPAddress.TryParse does not.
        var candidate = host.StartsWith('[') && host.EndsWith(']') ? host[1..^1] : host;

        return IPAddress.TryParse(candidate, out var address) && IPAddress.IsLoopback(address);
    }
}
