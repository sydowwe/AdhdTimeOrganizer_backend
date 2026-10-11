using System.Reflection;
using AdhdTimeOrganizer.Core.application.seam;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
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
/// <see cref="IActivityMembershipSource"/> is resolved as a keyed <c>IEnumerable&lt;&gt;</c> and the
/// consumer pattern falls back to <em>not filtering</em> when no source matches the key — so a slice
/// dropped from <c>ModuleServiceExtensions.ModuleAssemblies</c>, a renamed key, or a duplicate key
/// silently widens the caller's query to every row. Likewise a seam event with no handler is simply
/// inert.
/// </para>
/// <para>
/// ⚠ <see cref="IActivityMembershipSource"/> currently has <b>no consumer</b>: its only one was
/// History's <c>grid</c> endpoint, deleted with <c>CQ-1</c> because no client ever called it. The two
/// implementations and the guards over them are kept deliberately — see
/// <see cref="TodoListMembershipSource_NarrowsToActivitiesOnAList"/>.
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
        AssertRegisteredOnce<ITodoListItemPlanSource>(sp);
        AssertRegisteredOnce<IFocusIntervalSource>(sp);
        AssertRegisteredOnce<IRunningTimerLookup>(sp);
    }

    /// <summary>
    /// The membership sources resolve, and their keys are <b>distinct</b> and cover
    /// <see cref="ActivityMembershipSourceKeys"/> exactly.
    /// </summary>
    /// <remarks>
    /// The consumer pattern this guards is a <c>FirstOrDefault(s =&gt; s.Key == key)</c> lookup that
    /// leaves the query <em>unfiltered</em> when it finds nothing — so a missing source does not throw,
    /// it just stops narrowing, and a duplicate key silently picks whichever implementation registered
    /// first. Comparing against the constants (rather than a hard-coded count) also means adding a key
    /// without shipping an implementation fails here rather than in production.
    /// <para>
    /// The consumer that made this load-bearing (History's <c>grid</c> endpoint) is gone; the guard
    /// stays because the sources and their keys are still registered and still exported to whatever
    /// consumes them next.
    /// </para>
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
            + "no source silently stops narrowing its consumer's query; a source with no key is dead code.");
    }

    /// <summary>
    /// The to-do source actually narrows: seeded two activities for one user, one carried by a
    /// <c>TodoListItem</c> and one not, its <c>ActivityIds</c> query returns the first and not the
    /// second.
    /// </summary>
    /// <remarks>
    /// Resolution alone is not the whole mechanism — a source can register under the right key and
    /// still hand back a query that matches everything (or nothing), and no consumer would throw. This
    /// used to be asserted end-to-end through the History grid
    /// (<c>HistoryRouteSmokeTests.Grid_MembershipFilter_NarrowsThroughTheSeam</c>), but that endpoint
    /// was deleted with <c>CQ-1</c> — no client ever called it — which as of today leaves
    /// <see cref="IActivityMembershipSource"/> with <b>no consumer at all</b>. The behaviour is pinned
    /// here rather than dropped so the next consumer inherits a working seam.
    /// </remarks>
    [Fact]
    public async Task TodoListMembershipSource_NarrowsToActivitiesOnAList()
    {
        const long userId = FakeLoggedUserService.TestUserId;

        long onListActivityId;
        long offListActivityId;

        await using var db = CreateDbContext();

        onListActivityId = await SeedActivityAsync(db, userId, "Seam on a to-do list");
        offListActivityId = await SeedActivityAsync(db, userId, "Seam not on any list");

        var priority = new TaskPriority { UserId = userId, Text = "Normal", Color = "#445566", Priority = 1 };
        db.Set<TaskPriority>().Add(priority);
        await db.SaveChangesAsync(CancellationToken);

        db.Set<TodoListItem>().Add(new TodoListItem
        {
            UserId = userId,
            ActivityId = onListActivityId,
            TaskPriorityId = priority.Id
        });
        await db.SaveChangesAsync(CancellationToken);

        using var scope = Fixture.AdminAndUserFactory.Services.CreateScope();
        var source = scope.ServiceProvider.GetServices<IActivityMembershipSource>()
            .Single(s => s.Key == ActivityMembershipSourceKeys.TodoList);

        var anyFacet = await source.ActivityIds(db, null).ToListAsync(CancellationToken);
        var matchingFacet = await source.ActivityIds(db, priority.Id).ToListAsync(CancellationToken);
        var otherFacet = await source.ActivityIds(db, priority.Id + 9_100).ToListAsync(CancellationToken);

        Assert.Contains(onListActivityId, anyFacet);
        Assert.DoesNotContain(offListActivityId, anyFacet);

        Assert.Contains(onListActivityId, matchingFacet);
        Assert.DoesNotContain(onListActivityId, otherFacet);
    }

    private static async Task<long> SeedActivityAsync(DbContext db, long userId, string name)
    {
        var role = new ActivityRole { UserId = userId, Name = $"{name} Role {Guid.NewGuid():N}", Color = "#112233" };
        db.Set<ActivityRole>().Add(role);
        await db.SaveChangesAsync(CancellationToken);

        var activity = new Activity { UserId = userId, Name = name, RoleId = role.Id };
        db.Set<Activity>().Add(activity);
        await db.SaveChangesAsync(CancellationToken);
        return activity.Id;
    }

    /// <summary>
    /// The activity <b>reference</b> sources resolve, their keys are distinct, and they cover
    /// <see cref="ActivityReferenceSourceKeys"/> exactly.
    /// </summary>
    /// <remarks>
    /// Same shape as the membership assertion above and a worse failure than it. This seam backs
    /// <c>usageCount</c> / <c>canDelete</c> on the activity grid and the repoint half of
    /// <c>POST /activity/merge</c>, and a missing source is silent in <em>both</em>: the count merely
    /// comes back lower — so an activity with a year of history reads "Records: 0" and offers an enabled
    /// delete button — and a merge leaves that slice's rows on activities the same request then deletes,
    /// cascading them away. Nothing throws and the response still looks plausible. There is no runtime
    /// check that can tell "genuinely unreferenced" from "the slice never registered", which is why the
    /// guard has to be here.
    /// </remarks>
    [Fact]
    public void ActivityReferenceSources_CoverEveryKey_Exactly()
    {
        using var scope = Fixture.AdminAndUserFactory.Services.CreateScope();
        var sources = scope.ServiceProvider.GetServices<IActivityReferenceSource>().ToList();

        var declaredKeys = typeof(ActivityReferenceSourceKeys)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        var resolvedKeys = sources.Select(s => s.Key).ToList();

        Assert.True(resolvedKeys.Distinct().Count() == resolvedKeys.Count,
            "Two IActivityReferenceSource implementations claim the same Key, so one of them never runs: "
            + string.Join(", ", sources.Select(s => $"{s.Key}={s.GetType().Name}")));

        Assert.True(declaredKeys.OrderBy(k => k).SequenceEqual(resolvedKeys.OrderBy(k => k)),
            $"ActivityReferenceSourceKeys declares [{string.Join(", ", declaredKeys.OrderBy(k => k))}] "
            + $"but the container resolved [{string.Join(", ", resolvedKeys.OrderBy(k => k))}]. A key with no "
            + "source means usageCount undercounts and merge strands that slice's rows on an activity it "
            + "then deletes; a source with no key is dead code.");
    }

    /// <summary>
    /// The <b>ownership</b> verifiers resolve, their keys are distinct, and they cover
    /// <see cref="UserOwnedReferenceKeys"/> exactly.
    /// </summary>
    /// <remarks>
    /// This one fails in the safe direction and still needs pinning. <c>ReferencedIdOwnershipService</c>
    /// looks a verifier up by key and answers <c>false</c> when it finds none, so a slice dropped from
    /// <c>ModuleAssemblies</c> or a renamed key turns every write carrying that id into a 404 —
    /// nothing throws, nothing logs, and the feature (saving a recording from a completed to-do item)
    /// simply stops working for everyone. A key with no verifier is that outage; a verifier with no key
    /// is a check nobody performs.
    /// </remarks>
    [Fact]
    public void UserOwnedReferenceVerifiers_CoverEveryKey_Exactly()
    {
        using var scope = Fixture.AdminAndUserFactory.Services.CreateScope();
        // The face the endpoints inject over them, registered the same way and for the same reason as
        // IActivityReferenceService: resolved singly, so a missing registration throws at activation.
        AssertRegisteredOnce<IReferencedIdOwnershipService>(scope.ServiceProvider);

        var verifiers = scope.ServiceProvider.GetServices<IUserOwnedReferenceVerifier>().ToList();

        var declaredKeys = typeof(UserOwnedReferenceKeys)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        var resolvedKeys = verifiers.Select(v => v.Key).ToList();

        Assert.True(resolvedKeys.Distinct().Count() == resolvedKeys.Count,
            "Two IUserOwnedReferenceVerifier implementations claim the same Key, so one of them never "
            + "runs: " + string.Join(", ", verifiers.Select(v => $"{v.Key}={v.GetType().Name}")));

        Assert.True(declaredKeys.OrderBy(k => k).SequenceEqual(resolvedKeys.OrderBy(k => k)),
            $"UserOwnedReferenceKeys declares [{string.Join(", ", declaredKeys.OrderBy(k => k))}] but the "
            + $"container resolved [{string.Join(", ", resolvedKeys.OrderBy(k => k))}]. A key with no "
            + "verifier fails closed — every write carrying that id becomes a 404.");
    }

    /// <summary>
    /// <c>IActivityReferenceService</c> — the single face the endpoints inject — resolves exactly once.
    /// </summary>
    /// <remarks>
    /// It carries only <c>IScopedService</c> as a lifetime marker, and the Scrutor scan registers
    /// <c>AsImplementedInterfaces()</c>, so a version of it that forgot to implement the contract
    /// interface would compile, register as <c>IScopedService</c> alone, and fail every activity grid
    /// request at endpoint activation.
    /// </remarks>
    [Fact]
    public void ActivityReferenceService_ResolvesExactlyOnce()
    {
        using var scope = Fixture.AdminAndUserFactory.Services.CreateScope();
        AssertRegisteredOnce<IActivityReferenceService>(scope.ServiceProvider);
    }

    /// <summary>
    /// The activity <b>removal</b> participants resolve, their keys are distinct, they cover
    /// <see cref="ActivityRemovalParticipantKeys"/> exactly, and the face the endpoints inject resolves once.
    /// </summary>
    /// <remarks>
    /// A missing participant is silent: the delete and the merge still succeed, and the slice's out-of-database
    /// state for the cascaded rows — a to-do's due reminder on the user's upcoming list — simply never hears of it.
    /// <c>TodoListItemDueReminderActivityLifecycleTests</c> asserts the rows; this pins the registration.
    /// </remarks>
    [Fact]
    public void ActivityRemovalParticipants_CoverEveryKey_Exactly()
    {
        using var scope = Fixture.AdminAndUserFactory.Services.CreateScope();
        AssertRegisteredOnce<IActivityRemovalService>(scope.ServiceProvider);

        var participants = scope.ServiceProvider.GetServices<IActivityRemovalParticipant>().ToList();
        var declaredKeys = typeof(ActivityRemovalParticipantKeys)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();
        var resolvedKeys = participants.Select(p => p.Key).ToList();

        Assert.True(resolvedKeys.Distinct().Count() == resolvedKeys.Count,
            "Two IActivityRemovalParticipant implementations claim the same Key: "
            + string.Join(", ", participants.Select(p => $"{p.Key}={p.GetType().Name}")));

        Assert.True(declaredKeys.OrderBy(k => k).SequenceEqual(resolvedKeys.OrderBy(k => k)),
            $"ActivityRemovalParticipantKeys declares [{string.Join(", ", declaredKeys.OrderBy(k => k))}] "
            + $"but the container resolved [{string.Join(", ", resolvedKeys.OrderBy(k => k))}].");
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