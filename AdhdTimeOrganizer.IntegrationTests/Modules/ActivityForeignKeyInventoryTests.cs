using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Modules;

/// <summary>
/// Freezes every foreign key in the solution whose principal is <see cref="Activity"/>: which entity
/// declares it, whether it is required, whether it is unique (i.e. a 1:1), and what happens to the
/// dependent row when the activity is deleted.
/// </summary>
/// <remarks>
/// <para>
/// <b>The policy this pins.</b> Archive (<c>PATCH /activity/{id}/archived</c>) is the non-destructive
/// path; a hard delete means destroy, and the cascade is the point. <c>activity_id</c> is <c>NOT NULL</c>
/// on 10 of these columns, so a dependent row genuinely cannot survive its activity — <c>Restrict</c>
/// there would make any activity that was ever used undeletable. Hence <c>Cascade</c> everywhere, with
/// exactly one deliberate exception: <c>TodoListItem.PairedLeisureActivityId</c> is the optional other
/// half of a temptation bundle, so losing the reward activity must not destroy the task.
/// </para>
/// <para>
/// <b>Frozen is not the same as settled.</b> This literal records what the model does today, which is not
/// everywhere the same thing as what somebody decided — see the note on <c>PomodoroTimerPreset</c> below
/// before reading any single row as intent.
/// </para>
/// <para>
/// <b>Why a frozen literal and not a rule.</b> "Every activity FK is Cascade" was asserted in
/// <c>MergeActivityEndpoint</c>'s remarks while it was quietly false — the two tracker pattern mappings
/// were optional with no explicit <c>OnDelete</c>, so EF's <c>ClientSetNull</c> default left them at
/// <c>NO ACTION</c> in the database, and <c>DELETE /api/activity/{id}</c> on a mapped activity answered
/// 409 naming no entity. Nothing failed to build and nothing logged; it took a live database to notice.
/// The same two were <c>WithOne()</c> rather than <c>WithMany()</c>, giving <c>activity_id</c> a unique
/// index that stopped a user mapping two executables onto one activity. Both slips are one word in a
/// configuration file, both are invisible in review, and both are silent at runtime — so the inventory
/// is written out by hand below and compared whole. A new activity FK fails this test by existing, which
/// is the intent: adding one is a decision about delete behaviour, not a detail.
/// </para>
/// <para>
/// Model-level, so no database rows are touched. It still runs in the Postgres collection because
/// building the real model needs the real host composition — which is also what makes it catch a slice
/// dropped from an assembly scan: those FKs would simply not be in the model, and the count would fall.
/// </para>
/// </remarks>
[Collection("Postgres")]
public class ActivityForeignKeyInventoryTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private sealed record ActivityFk(string Entity, string Property, bool IsRequired, bool IsUnique, DeleteBehavior OnDelete)
    {
        public override string ToString() =>
            $"{Entity}.{Property} (required: {IsRequired}, unique: {IsUnique}, {OnDelete})";
    }

    /// <summary>
    /// The 19 model edges into <see cref="Activity"/>. Seventeen are real database constraints; the last
    /// two are on the <c>PlannerSuggestionFrom*</c> query-type entities, which map to materialized views
    /// and therefore carry no constraint of their own — they are in the model all the same, and a change
    /// to their shape would move the generated SQL.
    /// </summary>
    private static readonly ActivityFk[] Expected =
    [
        // ActivityProfiles — the only 1:1s, and deliberately so: an activity has at most one profile of
        // each kind. This is what makes merge's profile rules collision rules rather than plain repoints.
        new("ActivityBacklogProfile", "ActivityId", true, true, DeleteBehavior.Cascade),
        new("ActivityBucketListProfile", "ActivityId", true, true, DeleteBehavior.Cascade),
        new("ActivityProjectProfile", "ActivityId", true, true, DeleteBehavior.Cascade),
        new("LeisureSuggestionRecord", "ActivityId", true, false, DeleteBehavior.Cascade),
        new("MemoryAnchor", "ActivityId", true, false, DeleteBehavior.Cascade),

        // Core.
        // TODO
        // ⚠ OPEN QUESTION, frozen here only to keep this list honest about the present. These are the
        // three nullable columns whose Cascade destroys a row that could have survived: deleting either
        // activity takes the whole PomodoroTimerPreset with it — its name, its five durations and its
        // cycle count — even though the column could simply go null, and even though losing the *break*
        // activity is not the same event as losing the activity the preset is about. SetNull is
        // available (it is what the structurally identical TodoListItem.PairedLeisureActivityId uses)
        // and was deliberately left out of scope when the tracker mappings were fixed, not judged and
        // kept. TimerPreset is the weaker case of the same question — a bare timer preset with no
        // activity may be meaningless, which is a product call. Decide these on purpose; until then do
        // not read these three rows as somebody's intent.
        new("PomodoroTimerPreset", "FocusActivityId", false, false, DeleteBehavior.Cascade),
        new("PomodoroTimerPreset", "RestActivityId", false, false, DeleteBehavior.Cascade),
        new("TimerPreset", "ActivityId", false, false, DeleteBehavior.Cascade),

        // History
        new("ActivityHistory", "ActivityId", true, false, DeleteBehavior.Cascade),

        // Planning — three task shapes, each its own table, plus the two view entities.
        new("PlannerTask", "ActivityId", true, false, DeleteBehavior.Cascade),
        new("RepeatingPlannerTask", "ActivityId", true, false, DeleteBehavior.Cascade),
        new("TemplatePlannerTask", "ActivityId", true, false, DeleteBehavior.Cascade),
        new("PlannerSuggestionFromActivityHistory", "ActivityId", true, false, DeleteBehavior.Cascade),
        new("PlannerSuggestionFromPlannerTask", "ActivityId", true, false, DeleteBehavior.Cascade),

        // Routines
        new("RoutineTodoList", "ActivityId", true, false, DeleteBehavior.Cascade),

        // TodoLists. PairedLeisureActivityId is the one SetNull in the family: the paired reward activity
        // is optional, so losing it must blank the pairing rather than destroy the task.
        new("TodoListItem", "ActivityId", true, false, DeleteBehavior.Cascade),
        new("TodoListItem", "PairedLeisureActivityId", false, false, DeleteBehavior.SetNull),

        // Tracking. N:1 and Cascade like the rest — see TrackerPatternMappingActivityFkTests for the
        // behavioural half. SetNull is not available here: CK_Tracker*MappingByPattern_TargetRequired
        // requires exactly one of ignored / activity / role-or-category, so a blanked mapping fails the
        // check constraint rather than surviving as an unmapped rule.
        new("TrackerAndroidMappingByPattern", "ActivityId", false, false, DeleteBehavior.Cascade),
        new("TrackerDesktopMappingByPattern", "ActivityId", false, false, DeleteBehavior.Cascade)
    ];

    [Fact]
    public void EveryForeignKeyIntoActivity_MatchesTheFrozenInventory()
    {
        using var db = CreateDbContext();

        var actual = db.Model.GetEntityTypes()
            .SelectMany(e => e.GetForeignKeys())
            .Where(fk => fk.PrincipalEntityType.ClrType == typeof(Activity))
            .SelectMany(fk => fk.Properties.Select(p => new ActivityFk(
                fk.DeclaringEntityType.ClrType.Name,
                p.Name,
                !p.IsNullable,
                fk.IsUnique,
                fk.DeleteBehavior)))
            .OrderBy(fk => fk.Entity, StringComparer.Ordinal)
            .ThenBy(fk => fk.Property, StringComparer.Ordinal)
            .ToList();

        var expected = Expected
            .OrderBy(fk => fk.Entity, StringComparer.Ordinal)
            .ThenBy(fk => fk.Property, StringComparer.Ordinal)
            .ToList();

        var missing = expected.Except(actual).ToList();
        var unexpected = actual.Except(expected).ToList();

        Assert.True(missing.Count == 0 && unexpected.Count == 0,
            "The set of foreign keys into Activity has changed. This is a decision about what a delete "
            + "destroys, not a detail — read the policy in this file's remarks, then update the literal.\n"
            + "  no longer in the model (or changed shape): " + Describe(missing) + "\n"
            + "  in the model but not in the inventory:     " + Describe(unexpected));

        // Guards the pair of edges that share a declaring entity (TodoListItem, PomodoroTimerPreset):
        // Except() is set-based, so a duplicated row would otherwise cancel out against a lost one.
        Assert.Equal(expected.Count, actual.Count);
    }

    private static string Describe(IReadOnlyCollection<ActivityFk> fks) =>
        fks.Count == 0 ? "(none)" : string.Join("; ", fks.Select(fk => fk.ToString()));
}