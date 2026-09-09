using AdhdTimeOrganizer.TodoLists.infrastructure.persistence.seeder.dev;
using FluentAssertions;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// <c>TEST-13</c> — a divergence guard, not a behaviour test. <c>TodoListItemSeeder</c> used to inline its
/// own copy of <c>GetNextDisplayOrder</c>'s arithmetic once per priority, so it carried private copies of
/// both <c>CQ-2</c> (the <c>?? 0</c> / <c>!= 0</c> sentinel) and <c>CQ-3</c> (the <c>(int?)</c> cast over a
/// <c>bigint</c> column) into three more places. It now calls the extension — and the failure mode this
/// pins is that a <em>future</em> fix to the extension silently does not reach the seeder again, because
/// nothing links the two.
/// </summary>
/// <remarks>
/// <para>
/// A source-text assertion rather than an integration test, deliberately. The seeder is dev-only fixture
/// data: running it proves nothing a user would ever see, and the regression that actually happens is
/// <b>divergence</b> — someone re-inlining the arithmetic — not wrongness. That is a property of the text,
/// so the text is what is asserted, the same way <c>Infrastructure/ModuleSchemaTests</c> and
/// <c>Modules/ModuleWiringTests</c> assert on structure rather than on a round trip.
/// </para>
/// <para>
/// The file is located by name under the slice rather than by a fixed relative path, so moving it within
/// the project does not turn this into a false failure — and a file that cannot be found fails loudly
/// instead of vacuously passing, which is the usual way a guard like this stops guarding.
/// </para>
/// <para>
/// Needs no database and no host, so it is outside the <c>Postgres</c> collection.
/// </para>
/// </remarks>
public class TodoListItemSeederDisplayOrderGuardTests
{
    [Fact(DisplayName = "TEST-13: the dev TodoListItemSeeder computes its next DisplayOrder through GetNextDisplayOrder, never with its own MIN")]
    public void DevSeeder_DoesNotReimplementGetNextDisplayOrder()
    {
        var source = File.ReadAllText(SeederSourcePath());

        source.Should().NotContain("MinAsync",
            "the seeder must not take its own MIN over DisplayOrder — an inlined copy of the arithmetic is how CQ-2 and CQ-3 survived a fix to TodoListExtensions the first time");

        // Positive control. Without it, deleting the seeding altogether would pass the assertion above.
        source.Should().Contain("GetNextDisplayOrder",
            "the seeder is expected to still mint display orders, and to do it through the one implementation");
    }

    /// <summary>
    /// Walks up from the test assembly to the repository root (the directory holding the solution file),
    /// which works from both <c>bin/Debug</c> and the relocated <c>artifacts-cli</c> tree the CLI builds
    /// into.
    /// </summary>
    private static string SeederSourcePath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AdhdTimeOrganizer.sln")))
            directory = directory.Parent;

        directory.Should().NotBeNull("the guard reads the seeder's source, so it needs the repository root above {0}", AppContext.BaseDirectory);

        var sliceDirectory = Path.Combine(directory!.FullName, "AdhdTimeOrganizer.TodoLists");
        var fileName = $"{nameof(TodoListItemSeeder)}.cs";

        var matches = Directory.GetFiles(sliceDirectory, fileName, SearchOption.AllDirectories);

        matches.Should().ContainSingle(
            "exactly one {0} must exist in the TodoLists slice — zero means this guard has stopped guarding anything, more than one means it is guarding the wrong copy", fileName);

        return matches[0];
    }
}
