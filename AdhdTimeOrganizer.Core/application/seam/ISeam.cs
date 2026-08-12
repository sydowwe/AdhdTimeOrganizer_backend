using FastEndpoints;

namespace AdhdTimeOrganizer.Core.application.seam;

/// <summary>
/// Marks an interface as a <b>cross-slice seam</b>: a contract declared in Core so that one slice can
/// reach another's rows without either slice referencing the other.
/// </summary>
/// <remarks>
/// <para>
/// This carries no members and changes no behaviour. It exists so the whole seam surface is
/// discoverable from one place — "Find Usages" / the type hierarchy on <see cref="ISeam"/> lists every
/// seam in the solution, which a folder convention alone cannot guarantee. The folder
/// (<c>application/seam/</c>) and this marker are enforced together by
/// <c>SeamWiringTests.Seams_AreDeclaredInCore_AndImplementedInSeamFolders</c>.
/// </para>
/// <para>
/// ⚠ <b>Do not use this as a DI lifetime marker.</b> Implementations still carry
/// <c>IScopedService</c> — the Scrutor scans in <c>ModuleServiceExtensions</c> key off that, and
/// registering by <see cref="ISeam"/> would make every seam resolvable as every other one.
/// </para>
/// <para>
/// When you add a seam: derive its interface from this, declare it here in
/// <c>AdhdTimeOrganizer.Core.application.seam</c>, put the implementation in the owning slice's own
/// <c>application/seam/</c> folder, and add a row to <c>seam/README.md</c>. Decide between a seam and
/// an <see cref="ISeamEvent"/> by who owns the transaction — see that type's remarks.
/// </para>
/// </remarks>
public interface ISeam;

/// <summary>
/// Marks a cross-slice <b>event</b> — the other half of the seam surface, for the cases an
/// <see cref="ISeam"/> interface cannot express.
/// </summary>
/// <remarks>
/// <para>
/// Pick between the two by <b>who owns the transaction and who owns the decision</b>:
/// </para>
/// <list type="bullet">
/// <item>
/// <see cref="ISeam"/> — the caller needs a result back, or needs the work to land inside its own
/// <c>SaveChanges</c>. <c>IActivityTimeAttributionSink</c> is the example: it mutates the caller's
/// <c>DbContext</c> and deliberately does not save.
/// </item>
/// <item>
/// <see cref="ISeamEvent"/> — the caller has already committed and the <em>decision</em> belongs to
/// someone else. <c>ActivityTimeRecordedEvent</c> is the example: Tracking reports what it observed
/// and must not know the completion rule. This is the only shape that can invert a <b>write</b>
/// dependency, which is why the Tracking slice has no outbound slice edges.
/// </item>
/// </list>
/// <para>
/// ⚠ Handlers subscribe by concrete event type, so a seam event with no handler is silent — nothing
/// fails to build and nothing throws. Two such records (<c>ActivityAddedToTodoListEvent</c>,
/// <c>ActivityAddedToRoutineTodoListEvent</c>) sat here unpublished and unhandled until they were
/// deleted. <c>SeamWiringTests.SeamEvents_AllHaveAHandler</c> is what keeps that from recurring.
/// </para>
/// </remarks>
public interface ISeamEvent : IEvent;
