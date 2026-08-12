using System.Reflection;
using AdhdTimeOrganizer.Core.application.seam;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FastEndpoints;
using Microsoft.Extensions.DependencyInjection;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Modules;

/// <summary>
/// Composition-root tests for the <b>cross-slice seams</b> — the contracts in
/// <c>AdhdTimeOrganizer.Core/application/seam/</c> and <c>../event/</c> that let one slice reach
/// another's rows without referencing it. The registry they pin is
/// <c>AdhdTimeOrganizer.Core/application/seam/README.md</c>.
/// <para>
/// Every failure mode here is invisible to the compiler, and only one of them throws at runtime. The
/// three single-service seams (<see cref="IActivityTimeAttributionSink"/>,
/// <see cref="ICalendarDayLookup"/>, <see cref="ITodoListItemLoggedTimeSource"/>) fail at endpoint
/// activation, which is deliberate. But
/// <see cref="IActivityMembershipSource"/> is resolved as a keyed <c>IEnumerable&lt;&gt;</c> and its
/// consumer falls back to <em>not filtering</em> when no source matches the key — so a slice dropped
/// from <c>ModuleServiceExtensions.ModuleAssemblies</c>, a renamed key, or a duplicate key silently
/// widens the History grid to every row. Likewise a seam event with no handler is simply inert.
/// </para>
/// </summary>
[Collection("Postgres")]
public class SeamWiringTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    /// <summary>
    /// The slice + host assemblies. Taken from the loaded set rather than from
    /// <c>ModuleServiceExtensions.ModuleAssemblies</c> (which is <c>internal</c> to the host): the
    /// fixture has booted the real <c>Program</c> by the time any test here runs, so every slice is
    /// loaded — and an assembly that is somehow <em>not</em> loaded is exactly the fault these tests
    /// are looking for, which the count assertion below catches.
    /// </summary>
    private static Assembly[] SolutionAssemblies =>
        AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => a.GetName().Name?.StartsWith("AdhdTimeOrganizer", StringComparison.Ordinal) == true)
            .Where(a => a.GetName().Name != "AdhdTimeOrganizer.IntegrationTests")
            .ToArray();

    // ---- resolution -----------------------------------------------------------------------------

    /// <summary>
    /// The single-service seams resolve exactly once out of a real request scope. More than one
    /// implementation would mean the last registration silently wins; none would mean the consuming
    /// endpoint 500s on activation.
    /// </summary>
    [Fact]
    public void SingleSeams_ResolveExactlyOnce()
    {
        using var scope = Fixture.AdminAndUserFactory.Services.CreateScope();
        var sp = scope.ServiceProvider;

        AssertRegisteredOnce<IActivityTimeAttributionSink>(sp);
        AssertRegisteredOnce<ICalendarDayLookup>(sp);
        AssertRegisteredOnce<ITodoListItemLoggedTimeSource>(sp);
    }

    /// <summary>
    /// The membership sources resolve, and their keys are <b>distinct</b> and cover
    /// <see cref="ActivityMembershipSourceKeys"/> exactly.
    /// </summary>
    /// <remarks>
    /// This is the assertion that matters most in the file. <c>ApplyMembershipFilter</c> in
    /// <c>GetFilteredTableActivityHistoryEndpoint</c> looks a source up with
    /// <c>FirstOrDefault(s =&gt; s.Key == key)</c> and returns the query <em>unfiltered</em> when it
    /// finds nothing — so a missing source does not throw, it just stops narrowing, and a duplicate
    /// key silently picks whichever implementation registered first. Comparing against the constants
    /// (rather than a hard-coded count) also means adding a key without shipping an implementation
    /// fails here rather than in production.
    /// </remarks>
    [Fact]
    public void ActivityMembershipSources_CoverEveryKey_Exactly()
    {
        using var scope = Fixture.AdminAndUserFactory.Services.CreateScope();
        var sources = scope.ServiceProvider.GetServices<IActivityMembershipSource>().ToList();

        var declaredKeys = typeof(ActivityMembershipSourceKeys)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        var resolvedKeys = sources.Select(s => s.Key).ToList();

        Assert.True(resolvedKeys.Distinct().Count() == resolvedKeys.Count,
            "Two IActivityMembershipSource implementations claim the same Key, so one of them is "
            + "unreachable: " + string.Join(", ", sources.Select(s => $"{s.Key}={s.GetType().Name}")));

        Assert.True(declaredKeys.OrderBy(k => k).SequenceEqual(resolvedKeys.OrderBy(k => k)),
            $"ActivityMembershipSourceKeys declares [{string.Join(", ", declaredKeys.OrderBy(k => k))}] "
            + $"but the container resolved [{string.Join(", ", resolvedKeys.OrderBy(k => k))}]. A key with "
            + "no source silently stops narrowing the History grid; a source with no key is dead code.");
    }

    private static void AssertRegisteredOnce<TService>(IServiceProvider sp) where TService : class
    {
        var implementations = sp.GetServices<TService>().ToList();
        Assert.True(implementations.Count == 1,
            $"{typeof(TService).Name} resolved {implementations.Count} implementations: "
            + string.Join(", ", implementations.Select(i => i.GetType().Name)));
    }

    // ---- placement ------------------------------------------------------------------------------

    /// <summary>
    /// Every <see cref="ISeam"/> interface is declared in Core's seam namespace, and every
    /// implementation of one sits in its own slice's <c>application/seam/</c> folder.
    /// </summary>
    /// <remarks>
    /// The folder convention is what makes the seam surface reviewable, and a convention nothing
    /// enforces is a convention that drifts. Declaring a seam anywhere but Core would also
    /// reintroduce the slice→slice reference the seam exists to remove — that half would at least
    /// fail to build, but only once someone consumed it.
    /// </remarks>
    [Fact]
    public void Seams_AreDeclaredInCore_AndImplementedInSeamFolders()
    {
        const string coreSeamNamespace = "AdhdTimeOrganizer.Core.application.seam";
        var types = SolutionAssemblies.SelectMany(a => a.GetTypes()).ToList();

        var misplacedDeclarations = types
            .Where(t => t.IsInterface && typeof(ISeam).IsAssignableFrom(t) && t != typeof(ISeam))
            .Where(t => t.Namespace != coreSeamNamespace)
            .Select(t => t.FullName!)
            .ToList();

        Assert.True(misplacedDeclarations.Count == 0,
            "ISeam interfaces must be declared in " + coreSeamNamespace + " — found: "
            + string.Join(", ", misplacedDeclarations));

        var misplacedImplementations = types
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(ISeam).IsAssignableFrom(t))
            .Where(t => t.Namespace?.EndsWith(".application.seam", StringComparison.Ordinal) != true)
            .Select(t => t.FullName!)
            .ToList();

        Assert.True(misplacedImplementations.Count == 0,
            "Seam implementations must live in their slice's application/seam/ folder — found: "
            + string.Join(", ", misplacedImplementations));
    }

    /// <summary>
    /// Every <see cref="ISeamEvent"/> has at least one <see cref="IEventHandler{TEvent}"/>, and every
    /// cross-slice event record carries the marker.
    /// </summary>
    /// <remarks>
    /// Handlers subscribe by concrete event type, so an event nobody handles compiles, publishes, and
    /// does nothing. Two records — <c>ActivityAddedToTodoListEvent</c> and
    /// <c>ActivityAddedToRoutineTodoListEvent</c> — sat in <c>Core/application/event/</c> unpublished
    /// and unhandled, reading as live wiring, until they were deleted. This is the guard against a
    /// third.
    /// </remarks>
    [Fact]
    public void SeamEvents_AllHaveAHandler()
    {
        var types = SolutionAssemblies.SelectMany(a => a.GetTypes()).ToList();

        var handledEventTypes = types
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .SelectMany(t => t.GetInterfaces())
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEventHandler<>))
            .Select(i => i.GetGenericArguments()[0])
            .ToHashSet();

        var unhandled = types
            .Where(t => t is { IsAbstract: false } && typeof(ISeamEvent).IsAssignableFrom(t) && t != typeof(ISeamEvent))
            .Where(t => !handledEventTypes.Contains(t))
            .Select(t => t.Name)
            .ToList();

        Assert.True(unhandled.Count == 0,
            "These seam events have no IEventHandler<> and are therefore inert: "
            + string.Join(", ", unhandled));

        // The other direction: an event in Core's event folder that skipped the marker would not show
        // up in the type hierarchy the seam registry is read from.
        var unmarked = types
            .Where(t => t.Namespace == "AdhdTimeOrganizer.Core.application.event")
            .Where(t => typeof(IEvent).IsAssignableFrom(t) && !typeof(ISeamEvent).IsAssignableFrom(t))
            .Select(t => t.Name)
            .ToList();

        Assert.True(unmarked.Count == 0,
            "Cross-slice events must derive from ISeamEvent so they appear in the seam registry — found: "
            + string.Join(", ", unmarked));
    }
}
