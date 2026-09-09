using System.Reflection;
using System.Xml.Linq;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.desktop;
using FluentAssertions;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Modules;

/// <summary>
/// Freezes the one invariant the whole vertical-slice split rests on: <b>every slice references only
/// Core and the framework</b>, with exactly one deliberate slice→slice edge in the solution
/// (Routines → TodoLists).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is not already covered.</b> The seams in <c>Core/application/seam/</c> exist precisely so
/// that a slice never needs to see another slice, and <c>SeamWiringTests</c> pins that they are declared,
/// registered and consumed correctly. But nothing stopped somebody skipping the seam entirely: adding
/// <c>&lt;ProjectReference Include="..\AdhdTimeOrganizer.TodoLists..." /&gt;</c> to History compiles on the
/// first try, needs no wiring, and leaves every existing test green — including every seam test, because
/// the seams it bypasses go on working. The only guard that existed was
/// <c>ActivityProfilesRouteSmokeTests.Core_DoesNotReferenceActivityProfiles</c>, which covers one
/// direction of one slice.
/// </para>
/// <para>
/// <b>Two assertions, because each misses what the other catches.</b> The project files are the
/// declaration and are exact, but a reference can also arrive transitively — Routines legitimately pulls
/// TodoLists in, so anything TodoLists exposes is compile-visible to Routines' own consumers. The IL
/// check reads <see cref="Assembly.GetReferencedAssemblies"/>, which lists what the compiler actually
/// emitted a reference to, so it catches real coupling regardless of how the reference was declared. It
/// is the weaker of the two on its own: the C# compiler omits a project reference nothing uses, so a
/// declared-but-unused reference is invisible to it — which is exactly the state a new coupling passes
/// through on its way in.
/// </para>
/// <para>
/// Needs no database and no host, so it stays outside the <c>Postgres</c> collection.
/// </para>
/// </remarks>
public class SliceDependencyTests
{
    private const string Core = "AdhdTimeOrganizer.Core";
    private const string TodoLists = "AdhdTimeOrganizer.TodoLists";

    /// <summary>
    /// Every slice project, and the <c>AdhdTimeOrganizer.*</c> projects each one is allowed to reference.
    /// </summary>
    /// <remarks>
    /// <b>Adding an entry here is the decision, not a formality.</b> Before widening any row, read the
    /// four inversion patterns in <c>docs/claude/slices.md#cross-slice-seams</c> — a materialized view, a
    /// navigation-free FK declared in the host, an <c>IActivityMembershipSource</c>-shaped seam, or (for a
    /// write) an event whose decision moves to a subscriber. All four are in use, and every cross-slice
    /// dependency predicted to be unavoidable during the split turned out to fit one of them.
    /// <para>
    /// The single exception below is real rather than grandfathered: Routines builds on the shared to-do
    /// primitives TodoLists owns (<c>BaseTodoListItem</c>, <c>TodoListStep</c>, <c>BaseTodoListConfigure</c>,
    /// the toggle/step/reorder endpoint bases), which are base types and configuration helpers rather than
    /// rows — nothing a seam can hand across a boundary.
    /// </para>
    /// </remarks>
    private static readonly Dictionary<string, string[]> AllowedReferences = new()
    {
        [Core] = [],
        [TodoLists] = [Core],
        ["AdhdTimeOrganizer.History"] = [Core],
        ["AdhdTimeOrganizer.Planning"] = [Core],
        ["AdhdTimeOrganizer.Routines"] = [Core, TodoLists],
        ["AdhdTimeOrganizer.Tracking"] = [Core],
        ["AdhdTimeOrganizer.ActivityProfiles"] = [Core]
    };

    /// <summary>
    /// One loaded assembly per slice, keyed by project name. Pinned to a concrete type per slice rather
    /// than swept out of the <c>AppDomain</c>, so a slice that failed to load fails this test by throwing
    /// at class initialisation instead of quietly shrinking the set under assertion.
    /// </summary>
    private static readonly Dictionary<string, Assembly> SliceAssemblies = new()
    {
        [Core] = typeof(Activity).Assembly,
        [TodoLists] = typeof(TodoList).Assembly,
        ["AdhdTimeOrganizer.History"] = typeof(ActivityHistory).Assembly,
        ["AdhdTimeOrganizer.Planning"] = typeof(PlannerTask).Assembly,
        ["AdhdTimeOrganizer.Routines"] = typeof(RoutineTimePeriod).Assembly,
        ["AdhdTimeOrganizer.Tracking"] = typeof(DesktopActivityEntry).Assembly,
        ["AdhdTimeOrganizer.ActivityProfiles"] = typeof(ActivityBacklogProfile).Assembly
    };

    [Fact(DisplayName = "Every slice's project file declares only Core (and, for Routines, TodoLists)")]
    public void SliceProjectFiles_DeclareOnlyTheAllowedReferences()
    {
        var root = RepositoryRoot();

        foreach (var (slice, allowed) in AllowedReferences)
        {
            var projectFile = Path.Combine(root, slice, $"{slice}.csproj");
            File.Exists(projectFile).Should().BeTrue("{0} must exist for this guard to mean anything", projectFile);

            var declared = XDocument.Load(projectFile)
                .Descendants("ProjectReference")
                .Select(e => (string?)e.Attribute("Include"))
                .Where(include => !string.IsNullOrWhiteSpace(include))
                .Select(include => Path.GetFileNameWithoutExtension(include!.Replace('\\', Path.DirectorySeparatorChar)))
                .Where(name => name.StartsWith("AdhdTimeOrganizer", StringComparison.Ordinal))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();

            declared.Should().BeEquivalentTo(allowed,
                "{0} may reference only [{1}] — a slice→slice reference compiles fine and bypasses the seam that "
                + "exists to make it unnecessary. Read docs/claude/slices.md#cross-slice-seams before widening this.",
                slice, string.Join(", ", allowed));
        }
    }

    [Fact(DisplayName = "No slice assembly emits a reference to another slice")]
    public void SliceAssemblies_ReferenceOnlyTheAllowedSlices()
    {
        foreach (var (slice, assembly) in SliceAssemblies)
        {
            var referenced = assembly.GetReferencedAssemblies()
                .Select(a => a.Name)
                .Where(name => name is not null && name.StartsWith("AdhdTimeOrganizer", StringComparison.Ordinal))
                .Select(name => name!)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();

            referenced.Should().BeSubsetOf(AllowedReferences[slice],
                "{0} emitted a reference to a slice it may not see. The compiler only emits references that are "
                + "actually used, so this is real coupling, not a stale project file.", slice);
        }
    }

    /// <summary>
    /// Core is the hub every slice sits on, so a reference <em>out</em> of it inverts the direction the
    /// whole split depends on. Stated separately from the table above because it is the one that is
    /// tempting: <c>Activity</c>'s inverse navigations to the profiles, the to-do items and the history
    /// rows all used to exist, and each only fed a configuration helper a navigation expression.
    /// </summary>
    [Fact(DisplayName = "Core references no slice, in either the project file or the IL")]
    public void Core_ReferencesNoSlice()
    {
        AllowedReferences[Core].Should().BeEmpty("this test would be vacuous otherwise");

        typeof(Activity).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name)
            .Where(name => name is not null && name.StartsWith("AdhdTimeOrganizer", StringComparison.Ordinal))
            .Should().BeEmpty("Core must not reference any slice — see Activity.cs's note on the deleted inverse navigations");
    }

    /// <summary>
    /// Walks up from the test assembly to the repository root (the directory holding the solution file),
    /// which works from both <c>bin/Debug</c> and the relocated <c>artifacts-cli</c> tree the CLI builds
    /// into. Same shape as <c>TodoListItemSeederDisplayOrderGuardTests</c>.
    /// </summary>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AdhdTimeOrganizer.sln")))
            directory = directory.Parent;

        directory.Should().NotBeNull("this guard reads the slice project files, so it needs the repository root above {0}", AppContext.BaseDirectory);

        return directory!.FullName;
    }
}
