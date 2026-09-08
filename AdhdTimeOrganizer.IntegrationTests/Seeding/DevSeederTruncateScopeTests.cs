using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.IntegrationTests.Endpoints;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.IntegrationTests.Reminders;
using AdhdTimeOrganizer.Planning.application.service.reminder;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.model.entity.reminder;
using AdhdTimeOrganizer.Planning.infrastructure.persistence.seeder.dev;
using AdhdTimeOrganizer.Planning.infrastructure.persistence.seeder.userDefault;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sydowwe.Framework.infrastructure.persistence.seeder.@interface;
using Sydowwe.Framework.Testing;
using Sydowwe.Reminders.domain.entity;
using Sydowwe.Reminders.domain.@enum;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Seeding;

/// <summary>
/// TEST-23 — what <c>TRUNCATE … RESTART IDENTITY CASCADE</c> actually takes with it when Planning's two
/// truncating seeders run.
/// <para>
/// PostgreSQL's <c>CASCADE</c> is not the FK's <c>ON DELETE</c> action: it truncates every table that
/// references the target, outright, whatever that FK says. <c>SET NULL</c>, <c>RESTRICT</c> and
/// <c>CASCADE</c> all come out the same. So the blast radius of a dev reset is a property of the live FK
/// graph, not of anything written down in a configuration — and it changes silently every time a slice
/// adds a reference. These tests make it executable, so the next FK into <c>task_planner_day_template</c>
/// or <c>calendar</c> shows up as a failing assertion instead of as missing rows in someone's dev database.
/// </para>
/// <para>
/// ⚠ These truncate shared tables. Safe here for the reason <c>DevSeederTests</c> states:
/// <c>PostgresTestBase.InitializeAsync</c> Respawns the whole database before <em>every</em> test and a
/// collection's tests run sequentially, so no other test's rows exist while these run.
/// </para>
/// </summary>
[Collection("Postgres")]
public class DevSeederTruncateScopeTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;
    private const long OtherUserId = ReminderSeedHelper.OtherUserId;

    private const string CountInPlanning = "SELECT count(*) AS \"Value\" FROM planning.task_planner_day_template";
    private const string CountInPublic = "SELECT count(*) AS \"Value\" FROM public.task_planner_day_template";

    // ---- Scenario A — the finding (MIG-2) --------------------------------------------------------------

    /// <summary>
    /// <c>TaskPlannerDayTemplateSeeder.TruncateTable()</c> cascades into <c>user_planner_settings</c>,
    /// because <c>UserPlannerSettings.DefaultApplyTemplateId</c> references
    /// <c>task_planner_day_template</c>. The FK is <c>DeleteBehavior.SetNull</c>
    /// (<c>UserPlannerSettingsConfiguration.cs:36-39</c>), which is exactly the trap: it reads as "the
    /// setting is cleared", and what happens is that the whole row goes.
    /// <para>
    /// Nothing puts those rows back. <c>UserPlannerSettingsSeeder</c> is a per-user <b>default</b> seeder
    /// (order 420) that does not truncate and only runs at user setup, so a dev reset silently costs every
    /// user their slot duration, skip reasons, reminder lead time and conflict-resolution preference.
    /// </para>
    /// <para>
    /// Written green against current behaviour, per the convention: the assertion to flip is
    /// <c>settingsCount.Should().Be(0)</c> → <c>Be(2)</c>, and the two commented expectations below it
    /// become live at the same time. The fix that gets there is either nulling the FK before a
    /// non-cascading truncate, or re-running <c>UserPlannerSettingsSeeder</c> afterwards — the first keeps
    /// the user's other settings, which is the point.
    /// </para>
    /// </summary>
    [Fact(DisplayName = "MIG-2: truncating the day templates takes every user's planner settings with it")]
    public async Task TruncatingTheTemplates_AlsoEmptiesUserPlannerSettings()
    {
        long activityId;
        await using (var db = CreateDbContext())
        {
            await ReminderSeedHelper.EnsureOtherUserAsync(db, CancellationToken);

            activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "mig2-mine", UserId, CancellationToken);
            var otherActivityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "mig2-theirs", OtherUserId, CancellationToken);

            await SeedTemplateWithSettingsAsync(db, UserId, activityId);
            await SeedTemplateWithSettingsAsync(db, OtherUserId, otherActivityId);
        }

        using (var scope = NewScope())
            await TemplateSeederIn(scope).TruncateTable();

        await using var assertDb = CreateDbContext();

        (await assertDb.Set<TaskPlannerDayTemplate>().IgnoreQueryFilters().CountAsync(CancellationToken))
            .Should().Be(0, "the truncate's own table, for every user — only the re-seed that follows it is per-user");
        (await assertDb.Set<TemplatePlannerTask>().IgnoreQueryFilters().CountAsync(CancellationToken))
            .Should().Be(0, "cascade FK from the template; this one is intended");

        var settingsCount = await assertDb.Set<UserPlannerSettings>().IgnoreQueryFilters().CountAsync(CancellationToken);

        // FINDING: MIG-2 — flip this to Be(2) once the seeder stops cascading into user_planner_settings.
        settingsCount.Should().Be(0,
            "TRUNCATE ... CASCADE takes user_planner_settings outright — it does not honour the FK's ON DELETE SET NULL, "
            + "and nothing re-seeds those rows afterwards");

        // What the fix must produce instead, live once the assertion above flips:
        //
        //   var settings = await assertDb.Set<UserPlannerSettings>().IgnoreQueryFilters().ToListAsync(CancellationToken);
        //   settings.Should().OnlyContain(s => s.DefaultApplyTemplateId == null, "SetNull, not deleted");
        //   settings.Should().OnlyContain(s => s.SlotDurationMinutes == 37, "the user's other settings are untouched");

        // The other direction bounds the radius: CASCADE follows references *inwards* only. Activity is
        // referenced *by* template_planner_task rather than referencing the template, so it must survive —
        // a truncate that reached it would be emptying half of Core on a Planning dev reset.
        (await assertDb.Set<Activity>().IgnoreQueryFilters().CountAsync(CancellationToken))
            .Should().Be(2, "CASCADE walks referencing tables, never referenced ones");
        (await assertDb.Set<Activity>().IgnoreQueryFilters().AnyAsync(a => a.Id == activityId, CancellationToken))
            .Should().BeTrue();
    }

    // ---- Scenario B — CalendarSeeder's reach (the intended one) ----------------------------------------

    /// <summary>
    /// <c>CalendarSeeder.TruncateTable()</c> has the same shape and is meant to reach:
    /// <c>calendar</c> → <c>planner_task</c> (whose FK is <c>Restrict</c>, and CASCADE truncates it
    /// anyway) → <c>reminder</c>. That is the intended dev reset, and it is asserted here rather than
    /// assumed.
    /// <para>
    /// The asymmetry is the part worth knowing: the module-side <c>ReminderDefinition</c> lives in the
    /// <c>reminders</c> schema with no FK back to Planning, so no cascade reaches it and no code path
    /// cancels it either — the truncate is raw SQL, not a delete the registration service can observe.
    /// A dev reset therefore leaves the module holding <c>Active</c> definitions whose subjects no longer
    /// exist. That is the same asymmetry every planner-task delete path is written around
    /// (<c>domain-map.md</c> § <i>Reminders</i>, and
    /// <c>ReminderRegistrationTests.DeletingTheTask_CancelsItsReminder</c>, which is the path that
    /// <i>does</i> withdraw it). Acceptable in dev, provided it is written down — which is what this test
    /// is for; if it is ever made unacceptable, the assertion to change is the <c>Active</c> one.
    /// </para>
    /// <para>
    /// The template family is asserted intact for the same reason Scenario A asserts Activity is:
    /// <c>template_planner_task</c> is a sibling table that does not reference <c>calendar</c>, so a
    /// calendar reset must not touch it.
    /// </para>
    /// </summary>
    [Fact(DisplayName = "Truncating the calendar reaches planner tasks and portal reminders, and stops at the module boundary")]
    public async Task TruncatingTheCalendar_ReachesTasksAndReminders_ButNotTheModuleDefinition()
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);

        long templateId;
        await using (var db = CreateDbContext())
        {
            await ReminderSeedHelper.EnsureOtherUserAsync(db, CancellationToken);

            var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "calendar-reset-mine", UserId, CancellationToken);
            var calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, date, UserId, CancellationToken);
            await PlanningTestSeedHelper.SeedPlannerTaskAsync(
                db, activityId, calendarId, new TimeOnly(9, 0), new TimeOnly(10, 0), UserId, ct: CancellationToken);

            // A second user's day, so "app-wide" is asserted rather than inferred from the one user's rows.
            var otherActivityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "calendar-reset-theirs", OtherUserId, CancellationToken);
            var otherCalendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, date, OtherUserId, CancellationToken);
            var otherTaskId = await PlanningTestSeedHelper.SeedPlannerTaskAsync(
                db, otherActivityId, otherCalendarId, new TimeOnly(11, 0), new TimeOnly(12, 0), OtherUserId, ct: CancellationToken);
            await ReminderSeedHelper.SeedReminderAsync(
                db, OtherUserId, date.ToDateTime(new TimeOnly(11, 0)), "Theirs", otherTaskId, ct: CancellationToken);

            // Untouched by a calendar reset — a different table with no reference to calendar.
            templateId = await PlanningTestSeedHelper.SeedTaskPlannerDayTemplateAsync(db, UserId, "Survives", CancellationToken);
            await PlanningTestSeedHelper.SeedTemplatePlannerTaskAsync(
                db, templateId, activityId, new TimeOnly(8, 0), new TimeOnly(8, 30), UserId, CancellationToken);
        }

        // Through the endpoint, because that is the only path that writes both halves of the seam: the
        // portal Reminder row and the module's ReminderDefinition. A hand-built portal row would leave the
        // module side empty and the survival assertion below would prove nothing.
        long taskId;
        await using (var db = CreateDbContext())
            taskId = await db.Set<PlannerTask>().IgnoreQueryFilters()
                .Where(t => t.UserId == UserId).Select(t => t.Id).SingleAsync(CancellationToken);

        var created = await CreateClient().PostAsJsonAsync("api/reminder", new
        {
            Title = "Stand-up",
            RemindAt = DateTime.UtcNow.AddDays(1),
            LeadOffsetsMinutes = new[] { 0 },
            PlannerTaskId = taskId
        }, JsonOpts, CancellationToken);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var reminderId = await created.Content.ReadFromJsonAsync<long>(CancellationToken);

        await using (var beforeDb = CreateDbContext())
            (await DefinitionsForAsync(beforeDb, reminderId)).Should().ContainSingle(
                "the arrangement only means anything if the module side was registered in the first place");

        using (var scope = NewScope())
            await CalendarSeederIn(scope).TruncateTable();

        await using var assertDb = CreateDbContext();

        (await assertDb.Set<Calendar>().IgnoreQueryFilters().CountAsync(CancellationToken))
            .Should().Be(0, "the truncate's own table, for both users");
        (await assertDb.Set<PlannerTask>().IgnoreQueryFilters().CountAsync(CancellationToken))
            .Should().Be(0, "planner_task.calendar_id is Restrict, and TRUNCATE ... CASCADE ignores that entirely");
        (await assertDb.Set<Reminder>().IgnoreQueryFilters().CountAsync(CancellationToken))
            .Should().Be(0, "the cascade is recursive — reminder references planner_task, which references calendar");

        var definitions = await DefinitionsForAsync(assertDb, reminderId);
        definitions.Should().ContainSingle(
            "no FK crosses into the reminders schema, so the module's ledger cannot be reached by a cascade");
        definitions[0].Status.Should().Be(ReminderStatus.Active,
            "and nothing cancels it either: a raw TRUNCATE is invisible to the registration service, so a dev "
            + "reset leaves the module holding an active definition for a subject that no longer exists");

        (await assertDb.Set<TaskPlannerDayTemplate>().IgnoreQueryFilters().CountAsync(CancellationToken))
            .Should().Be(1, "the template family does not reference calendar");
        (await assertDb.Set<TemplatePlannerTask>().IgnoreQueryFilters().CountAsync(CancellationToken))
            .Should().Be(1, "PlannerTask, RepeatingPlannerTask and TemplatePlannerTask are three tables, not one");
    }

    // ---- Scenario C — schema qualification -------------------------------------------------------------

    /// <summary>
    /// <c>SeederExtensions.TruncateAsync</c> builds <c>"schema"."table"</c> from the EF model, and its own
    /// remarks say why: an unqualified name resolves against the connection's <c>search_path</c>, which is
    /// a 42P01 in the ordinary case and the wrong table in the nastier one where the name exists twice.
    /// Planning's tables are in <c>planning</c> (<c>ModuleSchemas.cs</c>, keyed by assembly —
    /// <c>ModuleSchemaTests</c> pins that half); this closes the loop from the model to the statement that
    /// actually runs.
    /// <para>
    /// The nasty case is constructed rather than described: a real <c>public.task_planner_day_template</c>
    /// with a row in it. The test connection carries no <c>SearchPath</c> (see
    /// <c>PostgresContainerFixture.ConnectionString</c>), so PostgreSQL's default <c>"$user", public</c>
    /// applies and an unqualified truncate would empty <b>that</b> table and leave Planning's alone — the
    /// exact inverse of what is asserted. Both counts are read through raw SQL naming the schema, because
    /// an EF read resolves through the model either way and could not tell the two apart.
    /// </para>
    /// </summary>
    [Fact(DisplayName = "The dev truncate names the planning schema, so a same-named public table is safe")]
    public async Task TruncateTable_IsSchemaQualified_AndCannotResolveThroughSearchPath()
    {
        await using (var db = CreateDbContext())
        {
            var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "search-path-probe", UserId, CancellationToken);
            await SeedTemplateWithSettingsAsync(db, UserId, activityId);

            await db.Database.ExecuteSqlRawAsync(
                "CREATE TABLE public.task_planner_day_template (id bigint NOT NULL)", CancellationToken);
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO public.task_planner_day_template (id) VALUES (1)", CancellationToken);
        }

        try
        {
            await using (var beforeDb = CreateDbContext())
            {
                (await CountRowsAsync(beforeDb, CountInPlanning)).Should().Be(1);
                (await CountRowsAsync(beforeDb, CountInPublic)).Should().Be(1);
            }

            using (var scope = NewScope())
                await TemplateSeederIn(scope).TruncateTable();

            await using var assertDb = CreateDbContext();

            (await CountRowsAsync(assertDb, CountInPlanning)).Should().Be(0,
                "the statement names planning explicitly; an unqualified one would have resolved to public and left this row here");
            (await CountRowsAsync(assertDb, CountInPublic)).Should().Be(1,
                "the decoy sits first on the default search_path — it is only safe because the name is qualified");
        }
        finally
        {
            await using var cleanupDb = CreateDbContext();
            await cleanupDb.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS public.task_planner_day_template", CancellationToken);
        }
    }

    // ---- helpers ----------------------------------------------------------------------------------------

    private IServiceScope NewScope() => Fixture.UnauthenticatedFactory.Services.CreateScope();

    /// <summary>
    /// The DI scan registers <c>IScopedService</c> implementations <c>AsImplementedInterfaces</c> only, so
    /// neither concrete seeder is resolvable directly — and asking for it by type off the seeder interface
    /// is also what proves it is still registered as that kind of seeder.
    /// </summary>
    private static TaskPlannerDayTemplateSeeder TemplateSeederIn(IServiceScope scope) =>
        scope.ServiceProvider.GetServices<IPerUserDevSeeder>().OfType<TaskPlannerDayTemplateSeeder>().Single();

    /// <summary>
    /// <c>CalendarSeeder</c> is an <c>IPerUserDefaultSeeder</c>, which declares no <c>TruncateTable</c> —
    /// a default seeder is not supposed to have one. It carries the method anyway, for the dev-reset flow,
    /// so it can only be reached through the concrete type.
    /// </summary>
    private static CalendarSeeder CalendarSeederIn(IServiceScope scope) =>
        scope.ServiceProvider.GetServices<IPerUserDefaultSeeder>().OfType<CalendarSeeder>().Single();

    /// <summary>
    /// One template, one template task hanging off it, and the user's settings row pointing at that
    /// template — with a distinctive <c>SlotDurationMinutes</c> so "the row survived with the user's own
    /// values" is distinguishable from "a default row was re-created".
    /// </summary>
    private async Task SeedTemplateWithSettingsAsync(DbContext db, long userId, long activityId)
    {
        var templateId = await PlanningTestSeedHelper.SeedTaskPlannerDayTemplateAsync(db, userId, $"Template {userId}", CancellationToken);
        await PlanningTestSeedHelper.SeedTemplatePlannerTaskAsync(
            db, templateId, activityId, new TimeOnly(9, 0), new TimeOnly(10, 0), userId, CancellationToken);

        db.Set<UserPlannerSettings>().Add(new UserPlannerSettings
        {
            UserId = userId,
            DefaultApplyTemplateId = templateId,
            SlotDurationMinutes = 37
        });
        await db.SaveChangesAsync(CancellationToken);
    }

    /// <summary>
    /// Takes the whole statement rather than a table name so nothing is interpolated at the
    /// <c>SqlQueryRaw</c> call site (EF1002), and so the schema is visible in the test that asserts on it.
    /// </summary>
    private async Task<long> CountRowsAsync(DbContext db, string countSql) =>
        await db.Database.SqlQueryRaw<long>(countSql).SingleAsync(CancellationToken);

    private async Task<List<ReminderDefinition>> DefinitionsForAsync(DbContext db, long reminderId) =>
        await db.Set<ReminderDefinition>()
            .Where(d => d.OwnerModule == ReminderRegistrationService.OwnerModule
                        && d.SubjectType == ReminderRegistrationService.SubjectType
                        && d.SubjectId == reminderId.ToString()
                        && d.Kind == ReminderRegistrationService.Kind)
            .ToListAsync(CancellationToken);
}
