using System.Reflection;
using FastEndpoints;

namespace Sydowwe.Framework.Testing;

/// <summary>
/// FastEndpoints resolves command/event handlers through a <b>process-global static</b> resolver
/// (<c>FastEndpoints.ServiceResolver._instance</c>) that points at whichever host most recently ran
/// <c>UseFastEndpoints()</c> during startup. That resolver captures <i>that host's</i> root
/// <see cref="IServiceProvider"/>.
/// <para>
/// With more than one <see cref="TestWebApplicationFactory{TProgram}"/> alive in a single test process this
/// is a footgun: a short-lived per-test host (built by <see cref="IPostgresFixture.CreateFactory"/> for a
/// cross-user / faked-handler scenario) hijacks the static resolver when it starts and, once it is disposed,
/// leaves the static pointing at a torn-down <see cref="IServiceProvider"/>. Every command executed
/// afterwards (e.g. <c>AssertEmployeeOwnershipCommand.ExecuteAsync</c> inside an attendance endpoint) then
/// throws <see cref="ObjectDisposedException"/> — which surfaces as spurious 500s on whatever tests happen
/// to run after the transient host, while those same tests pass in isolation.
/// </para>
/// The fix: capture the long-lived primary host's resolver once, and re-pin it after each transient host is
/// disposed. Reflection is required because the backing field is internal to FastEndpoints; this is pinned
/// to FastEndpoints 8.1.0's internal layout.
/// </summary>
internal static class FastEndpointsCommandBus
{
    private static readonly FieldInfo InstanceField =
        typeof(IServiceResolver).Assembly.GetType("FastEndpoints.ServiceResolver")?
            .GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
        ?? throw new InvalidOperationException(
            "FastEndpoints.ServiceResolver._instance was not found — the FastEndpoints internal layout has " +
            "changed. The multi-host command-bus resolver pin in the test harness (FastEndpointsCommandBus) " +
            "needs to be revisited for this FastEndpoints version.");

    /// <summary>The currently-pinned global command-bus resolver (the live host's), or null if unset.</summary>
    public static object? CaptureResolver() => InstanceField.GetValue(null);

    /// <summary>Re-pin the global command-bus resolver to a previously captured, still-alive host resolver.</summary>
    public static void PinResolver(object? resolver)
    {
        InstanceField.SetValue(null, resolver);
    }
}