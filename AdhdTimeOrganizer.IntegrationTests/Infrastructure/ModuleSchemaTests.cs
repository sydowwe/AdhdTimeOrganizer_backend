using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.infrastructure.persistence;
using Sydowwe.Framework.infrastructure.persistence.seeder;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Infrastructure;

/// <summary>
/// Pins which database schema every table lives in.
/// <para>
/// <see cref="SchemaPerModuleConvention"/> is exactly the kind of mechanism this solution keeps
/// getting bitten by: it runs at model finalization, it is driven by a CLR-assembly map, and when it
/// fails to place a table there is no build error and no log line — the table simply stays in
/// <c>public</c>, joined by every table added after it. That is not hypothetical. The first version
/// of the convention placed exactly one table out of fifty-six and the model still built, the
/// migration still generated, and the application still ran; only reading the emitted DDL showed it.
/// </para>
/// <para>
/// So the assertion is the full table list, read back out of <c>information_schema</c> after the
/// schema is really created, and compared against a literal. A test that recomputed the expectation
/// from <see cref="ModuleSchemas"/> would agree with the bug.
/// </para>
/// <para>
/// Note the fixture builds its schema with <c>EnsureCreated</c>, not migrations, so the partition
/// child tables the migration SQL generator emits for the two Tracking ledgers are absent here.
/// Their placement is covered by the parent's — a partition is always created in the schema its
/// <c>PARTITION OF</c> parent names.
/// </para>
/// </summary>
[Collection("Postgres")]
public class ModuleSchemaTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    /// <summary>
    /// Every table, by schema. Kept as a literal on purpose — see the class remarks. A table moving
    /// between schemas is a deliberate act and should have to be written down twice.
    /// </summary>
    private static readonly Dictionary<string, string[]> ExpectedTables = new()
    {
        // Core's timer presets are the only entity tables left in the default schema.
        [ModuleSchemas.Shared] = ["timer_preset", "pomodoro_timer_preset"],

        [ModuleSchemas.User] =
        [
            "user", "refresh_token", "AspNetRoles",
            "user__role", "user_claim", "user_login", "user_role_claim", "user_token"
        ],
        [ModuleSchemas.Activity] = ["activity", "activity_category", "activity_role"],
        [ModuleSchemas.TodoLists] = ["todo_list", "todo_list_item", "todo_list_category", "task_priority"],
        [ModuleSchemas.History] = ["activity_history"],
        [ModuleSchemas.Planning] =
        [
            "calendar", "planner_task", "repeating_planner_task", "template_planner_task",
            "task_importance", "task_planner_day_template", "user_planner_settings", "reminder"
        ],
        [ModuleSchemas.Routines] =
        [
            "routine_todo_list", "routine_time_period", "routine_period_completions", "user_routine_settings"
        ],
        [ModuleSchemas.Tracking] =
        [
            "desktop_activity_entry", "web_extension_activity_entry", "android_session_data",
            "tracker_desktop_mapping_by_pattern", "tracker_android_mapping_by_pattern"
        ],
        [ModuleSchemas.ActivityProfiles] =
        [
            "activity_backlog_profile", "activity_bucket_list_profile", "activity_project_profile",
            "activity_expected_cost_tier", "activity_experience_type", "activity_location_type",
            "activity_weather_dependency", "leisure_suggestion_record", "memory_anchor"
        ],
        [ModuleSchemas.Notifications] =
        [
            "notification", "notification_preference", "notification_quiet_hours", "push_subscription"
        ],
        [ModuleSchemas.Reminders] =
        [
            "reminder_definition", "reminder_recipient", "reminder_dispatch",
            "reminder_lead_offset", "reminder_occurrence_action", "reminder_kind_preference"
        ],
        [ModuleSchemas.Scheduler] = ["scheduled_job", "scheduled_job_run"],
        ["audit"] = ["business_audit_log"]
    };

    [Fact]
    public async Task EverySchemaHoldsExactlyItsOwnModulesTables()
    {
        var actual = await ReadTablesBySchemaAsync();

        actual.Should().BeEquivalentTo(ExpectedTables,
            "each module's tables belong in its own schema, and no schema may hold a table from another " +
            "module — a table missing from the schema it should be in has almost certainly stayed behind " +
            "in 'public' rather than been dropped");
    }

    /// <summary>
    /// The failure mode with teeth, stated directly: the convention silently no-opping leaves tables in
    /// the default schema, and the test above would then report them as missing from a dozen schemas
    /// without once saying where they actually went.
    /// </summary>
    [Fact]
    public async Task NoModuleTableIsLeftBehindInTheDefaultSchema()
    {
        var actual = await ReadTablesBySchemaAsync();

        actual.GetValueOrDefault(ModuleSchemas.Shared, [])
            .Should().BeEquivalentTo(ExpectedTables[ModuleSchemas.Shared],
                "every table except the timer presets belongs to a module schema");
    }

    /// <summary>
    /// The dev seeders' truncate helper has to name the schema too. It is the one piece of raw SQL in
    /// this area that no test would otherwise reach — seeders run only behind
    /// <c>Seeding:RunOnStartup</c> — and its unqualified form fails in two different ways depending on
    /// the `search_path`: a plain 42P01, or, where a table name exists in two schemas, truncating the
    /// wrong table and reporting success.
    /// </summary>
    [Fact]
    public async Task TruncateFindsTheTableInItsOwnSchema()
    {
        await using var db = fixture.CreateDbContext();

        db.Set<TodoListCategory>().Add(new TodoListCategory
        {
            UserId = FakeLoggedUserService.TestUserId,
            Name = $"Category-{Guid.NewGuid():N}",
            Color = "#abcdef"
        });
        await db.SaveChangesAsync(CancellationToken);

        // Guard the guard: a truncate that silently did nothing would also leave zero rows if there
        // were never any to begin with.
        (await db.Set<TodoListCategory>().CountAsync(CancellationToken)).Should().BeGreaterThan(0);

        // todo_list_category is in the 'todo' schema, so this only resolves if the helper qualifies it.
        await db.TruncateTableCascadeAsync<TodoListCategory>();

        (await db.Set<TodoListCategory>().CountAsync(CancellationToken)).Should().Be(0);
    }

    /// <summary>
    /// Every non-system base table, grouped by schema.
    /// </summary>
    /// <remarks>
    /// The schema list is not passed as a parameter and filtered in SQL: <c>= ANY({0})</c> over a
    /// <c>string[]</c> does not bind as an array through <c>SqlQueryRaw</c> and Postgres rejects it
    /// with 42809. Filtering the handful of rows in C# is simpler than fighting that, and reading
    /// <i>every</i> schema rather than only the expected ones means a table landing in a schema nobody
    /// declared shows up as an unexpected group instead of vanishing from the comparison.
    /// </remarks>
    private async Task<Dictionary<string, string[]>> ReadTablesBySchemaAsync()
    {
        await using var db = CreateDbContext();

        // One qualified string rather than a two-column shape: the scalar overload is the one every
        // other raw query in this suite uses, and it needs no result type to be bound.
        var qualified = await db.Database
            .SqlQueryRaw<string>(
                """
                SELECT table_schema || '.' || table_name AS "Value"
                FROM information_schema.tables
                WHERE table_type = 'BASE TABLE'
                  AND table_schema NOT IN ('pg_catalog', 'information_schema')
                """)
            .ToListAsync(CancellationToken);

        return qualified
            .Select(name => name.Split('.', 2))
            // EnsureCreated does not write it, but a migrated database has it and it belongs to no module.
            .Where(parts => parts[1] != "__EFMigrationsHistory")
            .GroupBy(parts => parts[0], parts => parts[1])
            .ToDictionary(g => g.Key, g => g.ToArray());
    }

    /// <summary>
    /// The three suggestion-pattern materialized views, which no migration creates — they are
    /// hand-written SQL installed by <c>SuggestionPatternViewInstaller</c>, so their schema is whatever
    /// the scripts say and not what the model says. Those two agreeing is exactly what needs pinning:
    /// EF reads the views through <c>ToView</c> in <c>planning</c>, and if a script created them
    /// somewhere else every suggestion query would fail with 42P01 at runtime while the model built
    /// perfectly.
    /// <para>
    /// <c>pg_matviews</c>, not <c>information_schema.tables</c> — the SQL standard has no concept of a
    /// materialized view, so it does not list them at all.
    /// </para>
    /// </summary>
    [Fact]
    public async Task TheSuggestionPatternViewsLiveInThePlanningSchema()
    {
        await using var db = CreateDbContext();

        var views = await db.Database
            .SqlQueryRaw<string>(
                """
                SELECT schemaname || '.' || matviewname AS "Value"
                FROM pg_matviews
                WHERE schemaname NOT IN ('pg_catalog', 'information_schema')
                """)
            .ToListAsync(CancellationToken);

        views.Should().BeEquivalentTo(
        [
            $"{ModuleSchemas.Planning}.mv_planner_task_pattern",
            $"{ModuleSchemas.Planning}.mv_activity_history_pattern",
            $"{ModuleSchemas.Planning}.mv_template_suggestion_pattern"
        ],
            "the fixture runs the real installer, so all three must exist and none may be left in 'public'");
    }
}
