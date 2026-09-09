using AdhdTimeOrganizer.Core.infrastructure.persistence;
using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.@enum;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.Core.domain.model.entity.timer;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.desktop;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using AdhdTimeOrganizer.Core.domain.model.entity.@base;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.domain.valueObject;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// A9, merge half — <c>POST /activity/merge</c>.
/// </summary>
/// <remarks>
/// <para>
/// The app manufactures duplicates from three directions (quick-create in four dialogs, quick-edit in
/// Clone mode, inline create on the picker) and until now had no way to clean them up, so "Reading",
/// "reading" and "Reading - kópia" are three activities with three separate history trails that every
/// dashboard groups separately.
/// </para>
/// <para>
/// <b>The load-bearing assertion in this file is
/// <see cref="Merge_RepointsEveryReferencingSlice"/>.</b> Repointing spans seven
/// <c>IActivityReferenceSource</c> implementations in six projects, resolved by string key, and a source
/// that is missing or misregistered fails <em>silently</em>: its rows are left pointing at an activity
/// the same request then deletes, and every activity FK in the solution is <c>Cascade</c> bar one
/// (<c>TodoListItem.PairedLeisureActivityId</c>, <c>SetNull</c>), so they are destroyed rather than
/// orphaned — see <c>ActivityForeignKeyInventoryTests</c>, which freezes that set. The response still
/// reports a plausible-looking count. That is why these assert on surviving rows in each slice, never
/// on <c>repointedCount</c>.
/// </para>
/// </remarks>
[Collection("Postgres")]
public class ActivityMergeTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private static long UserId => FakeLoggedUserService.TestUserId;

    private sealed record MergeResult(long SurvivorId, int MergedCount, int RepointedCount);

    private async Task<long> SeedActivityAsync(DbContext db, string name, bool isArchived = false, long? roleId = null, long? categoryId = null)
    {
        if (roleId == null)
        {
            var role = new ActivityRole { UserId = UserId, Name = $"{name} Role {Guid.NewGuid():N}", Color = "#112233" };
            db.Set<ActivityRole>().Add(role);
            await db.SaveChangesAsync(CancellationToken);
            roleId = role.Id;
        }

        var activity = new Activity
        {
            UserId = UserId,
            Name = name,
            RoleId = roleId.Value,
            CategoryId = categoryId,
            IsArchived = isArchived
        };
        db.Set<Activity>().Add(activity);
        await db.SaveChangesAsync(CancellationToken);
        return activity.Id;
    }

    private async Task<HttpResponseMessage> MergeAsync(long survivorId, params long[] mergedIds) =>
        await CreateClient().PostAsJsonAsync("api/activity/merge", new { SurvivorId = survivorId, MergedIds = mergedIds }, JsonOpts);

    // ---- the one that matters --------------------------------------------------------------------

    /// <summary>
    /// One merge, one referencing row per slice, and every one of them must end up on the survivor.
    /// </summary>
    /// <remarks>
    /// Six projects are represented here — Core (timer preset), History, TodoLists, Routines, Planning
    /// and Tracking. ActivityProfiles has its own tests below because its rules are collision rules
    /// rather than plain repoints.
    /// </remarks>
    [Fact]
    public async Task Merge_RepointsEveryReferencingSlice()
    {
        await using var db = CreateDbContext();
        var survivorId = await SeedActivityAsync(db, "Reading");
        var duplicateId = await SeedActivityAsync(db, "reading");

        // Core
        var preset = new TimerPreset { UserId = UserId, Duration = 1500, ActivityId = duplicateId };
        db.Set<TimerPreset>().Add(preset);

        // History
        var history = new ActivityHistory
        {
            UserId = UserId,
            ActivityId = duplicateId,
            StartTimestamp = DateTime.UtcNow.AddHours(-2),
            Length = new IntTime(0, 30),
            EndTimestamp = DateTime.UtcNow.AddMinutes(-90)
        };
        db.Set<ActivityHistory>().Add(history);

        // Tracking
        var mapping = new TrackerDesktopMappingByPattern
        {
            UserId = UserId,
            ProcessName = $"reader-{Guid.NewGuid():N}.exe",
            // The check constraint pairs each pattern column with its match type — a pattern without one
            // is rejected at the database, not at the endpoint.
            ProcessNameMatchType = PatternMatchType.Exact,
            IsActive = true,
            ActivityId = duplicateId
        };
        db.Set<TrackerDesktopMappingByPattern>().Add(mapping);
        await db.SaveChangesAsync(CancellationToken);

        // TodoLists
        var categoryId = await TodoListTestSeedHelper.SeedTodoListCategoryAsync(db, ct: CancellationToken);
        var todoListId = await TodoListTestSeedHelper.SeedTodoListAsync(db, categoryId: categoryId, ct: CancellationToken);
        var priorityId = await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, 1, ct: CancellationToken);
        var itemId = await TodoListTestSeedHelper.SeedTodoListItemAsync(db, duplicateId, priorityId, todoListId, ct: CancellationToken);

        // Routines
        var periodId = await TodoListTestSeedHelper.SeedRoutineTimePeriodAsync(db, ct: CancellationToken);
        var routineId = await TodoListTestSeedHelper.SeedRoutineTodoListAsync(db, duplicateId, periodId, ct: CancellationToken);

        // Planning — all three task shapes, because each is its own table
        var calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, DateOnly.FromDateTime(DateTime.UtcNow), ct: CancellationToken);
        var taskId = await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, duplicateId, calendarId, new TimeOnly(9, 0), new TimeOnly(10, 0), ct: CancellationToken);
        var repeatingId = await PlanningTestSeedHelper.SeedRepeatingPlannerTaskAsync(db, duplicateId, ct: CancellationToken);
        var templateId = await PlanningTestSeedHelper.SeedTaskPlannerDayTemplateAsync(db, ct: CancellationToken);
        var templateTaskId = await PlanningTestSeedHelper.SeedTemplatePlannerTaskAsync(db, templateId, duplicateId, new TimeOnly(11, 0), new TimeOnly(12, 0), ct: CancellationToken);

        var response = await MergeAsync(survivorId, duplicateId);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var assertDb = CreateDbContext();

        (await assertDb.Set<TimerPreset>().FirstAsync(p => p.Id == preset.Id, CancellationToken))
            .ActivityId.Should().Be(survivorId, "Core's own timer presets go through the same seam as everything else");
        (await assertDb.Set<ActivityHistory>().FirstAsync(h => h.Id == history.Id, CancellationToken))
            .ActivityId.Should().Be(survivorId, "recorded time is the thing the whole feature exists to preserve");
        (await assertDb.Set<TrackerDesktopMappingByPattern>().FirstAsync(m => m.Id == mapping.Id, CancellationToken))
            .ActivityId.Should().Be(survivorId, "a stranded mapping silently stops attributing every future heartbeat");
        (await assertDb.Set<TodoListItem>().FirstAsync(i => i.Id == itemId, CancellationToken))
            .ActivityId.Should().Be(survivorId);
        (await assertDb.Set<RoutineTodoList>().FirstAsync(r => r.Id == routineId, CancellationToken))
            .ActivityId.Should().Be(survivorId, "a routine carries streaks no re-created activity brings back");
        (await assertDb.Set<PlannerTask>().FirstAsync(t => t.Id == taskId, CancellationToken))
            .ActivityId.Should().Be(survivorId);
        (await assertDb.Set<RepeatingPlannerTask>().FirstAsync(t => t.Id == repeatingId, CancellationToken))
            .ActivityId.Should().Be(survivorId);
        (await assertDb.Set<TemplatePlannerTask>().FirstAsync(t => t.Id == templateTaskId, CancellationToken))
            .ActivityId.Should().Be(survivorId, "template tasks are their own table — forgetting one shape is a silent undercount");

        (await assertDb.Set<Activity>().IgnoreQueryFilters().AnyAsync(a => a.Id == duplicateId, CancellationToken))
            .Should().BeFalse("the merged-away activities are deleted, not archived — the dialog says they are gone");
    }

    [Fact]
    public async Task Merge_ReportsWhatItDid()
    {
        await using var db = CreateDbContext();
        var survivorId = await SeedActivityAsync(db, "Survivor");
        var firstId = await SeedActivityAsync(db, "First Duplicate");
        var secondId = await SeedActivityAsync(db, "Second Duplicate");

        db.Set<TimerPreset>().Add(new TimerPreset { UserId = UserId, Duration = 60, ActivityId = firstId });
        db.Set<TimerPreset>().Add(new TimerPreset { UserId = UserId, Duration = 120, ActivityId = secondId });
        await db.SaveChangesAsync(CancellationToken);

        var response = await MergeAsync(survivorId, firstId, secondId);
        var result = (await response.Content.ReadFromJsonAsync<MergeResult>(JsonOpts, CancellationToken))!;

        result.SurvivorId.Should().Be(survivorId);
        result.MergedCount.Should().Be(2);
        result.RepointedCount.Should().Be(2);
    }

    // ---- the collision cases ---------------------------------------------------------------------

    /// <summary>
    /// One to-do item pointing at <em>two</em> of the merged activities — through <c>ActivityId</c> and
    /// its <c>PairedLeisureActivityId</c> temptation bundle — must collapse to one reference. Not fail,
    /// and not duplicate the row.
    /// </summary>
    [Fact]
    public async Task Merge_OneRowReferencingTwoMergedActivities_CollapsesToOneReference()
    {
        await using var db = CreateDbContext();
        var survivorId = await SeedActivityAsync(db, "Reading Survivor");
        var taskDuplicateId = await SeedActivityAsync(db, "Reading Duplicate");
        var leisureDuplicateId = await SeedActivityAsync(db, "reading duplicate");

        var categoryId = await TodoListTestSeedHelper.SeedTodoListCategoryAsync(db, ct: CancellationToken);
        var todoListId = await TodoListTestSeedHelper.SeedTodoListAsync(db, categoryId: categoryId, ct: CancellationToken);
        var priorityId = await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, 1, ct: CancellationToken);
        var itemId = await TodoListTestSeedHelper.SeedTodoListItemAsync(db, taskDuplicateId, priorityId, todoListId, ct: CancellationToken);

        var item = await db.Set<TodoListItem>().FirstAsync(i => i.Id == itemId, CancellationToken);
        item.PairedLeisureActivityId = leisureDuplicateId;
        await db.SaveChangesAsync(CancellationToken);

        var response = await MergeAsync(survivorId, taskDuplicateId, leisureDuplicateId);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var assertDb = CreateDbContext();
        var merged = await assertDb.Set<TodoListItem>().Where(i => i.TodoListId == todoListId).ToListAsync(CancellationToken);

        merged.Should().ContainSingle("the item must not be duplicated");
        merged[0].ActivityId.Should().Be(survivorId);
        merged[0].PairedLeisureActivityId.Should().BeNull(
            "pairing an item with its own activity is not a temptation bundle, so the collapsed pairing is dropped");
    }

    /// <summary>
    /// Two to-do items on ONE list, each pointing at a different merged activity, must end up as one row
    /// on that list — not as a unique-index violation that rolls the whole merge back. This is the
    /// ordinary shape of a duplicate activity ("Reading" / "reading"), not an exotic one: if the user kept
    /// a task for each, the merge they are trying to perform is precisely the thing that collides.
    /// </summary>
    [Fact]
    public async Task Merge_TwoItemsOnOneList_CollapseToOneRow()
    {
        await using var db = CreateDbContext();
        var survivorId = await SeedActivityAsync(db, "Reading Survivor");
        var duplicateAId = await SeedActivityAsync(db, "Reading Duplicate");
        var duplicateBId = await SeedActivityAsync(db, "reading duplicate");

        var categoryId = await TodoListTestSeedHelper.SeedTodoListCategoryAsync(db, ct: CancellationToken);
        var todoListId = await TodoListTestSeedHelper.SeedTodoListAsync(db, categoryId: categoryId, ct: CancellationToken);
        var priorityId = await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, 350, ct: CancellationToken);
        await TodoListTestSeedHelper.SeedTodoListItemAsync(db, duplicateAId, priorityId, todoListId, ct: CancellationToken);
        await TodoListTestSeedHelper.SeedTodoListItemAsync(db, duplicateBId, priorityId, todoListId, ct: CancellationToken);

        var response = await MergeAsync(survivorId, duplicateAId, duplicateBId);
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "the unique (UserId, ActivityId, TodoListId) index must be collapsed onto, not tripped over: {0}",
            await response.Content.ReadAsStringAsync(CancellationToken));

        var result = (await response.Content.ReadFromJsonAsync<MergeResult>(JsonOpts, CancellationToken))!;
        result.RepointedCount.Should().Be(2,
            "the count the snackbar shows is rows moved — a collapsed row was still moved, same rule as a dropped pairing");

        await using var assertDb = CreateDbContext();
        var items = await assertDb.Set<TodoListItem>().Where(i => i.TodoListId == todoListId).ToListAsync(CancellationToken);

        items.Should().ContainSingle("two rows cannot share (UserId, ActivityId, TodoListId)");
        items[0].ActivityId.Should().Be(survivorId);

        (await assertDb.Set<Activity>().IgnoreQueryFilters().AnyAsync(a => a.Id == duplicateAId, CancellationToken))
            .Should().BeFalse();
        (await assertDb.Set<Activity>().IgnoreQueryFilters().AnyAsync(a => a.Id == duplicateBId, CancellationToken))
            .Should().BeFalse();
    }

    /// <summary>
    /// The positive control for the collapse above: items on <em>different</em> lists share no key, so
    /// nothing may be deleted. Without this, a fix that deletes too eagerly passes the collision test.
    /// </summary>
    [Fact]
    public async Task Merge_TwoItemsOnDifferentLists_BothSurvive()
    {
        await using var db = CreateDbContext();
        var survivorId = await SeedActivityAsync(db, "Reading Survivor");
        var duplicateAId = await SeedActivityAsync(db, "Reading Duplicate");
        var duplicateBId = await SeedActivityAsync(db, "reading duplicate");

        var categoryId = await TodoListTestSeedHelper.SeedTodoListCategoryAsync(db, ct: CancellationToken);
        var firstListId = await TodoListTestSeedHelper.SeedTodoListAsync(db, categoryId: categoryId, ct: CancellationToken);
        var secondListId = await TodoListTestSeedHelper.SeedTodoListAsync(db, categoryId: categoryId, ct: CancellationToken);
        var priorityId = await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, 350, ct: CancellationToken);
        var firstItemId = await TodoListTestSeedHelper.SeedTodoListItemAsync(db, duplicateAId, priorityId, firstListId, ct: CancellationToken);
        var secondItemId = await TodoListTestSeedHelper.SeedTodoListItemAsync(db, duplicateBId, priorityId, secondListId, ct: CancellationToken);

        var response = await MergeAsync(survivorId, duplicateAId, duplicateBId);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var assertDb = CreateDbContext();
        var items = await assertDb.Set<TodoListItem>()
            .Where(i => i.Id == firstItemId || i.Id == secondItemId)
            .ToListAsync(CancellationToken);

        items.Should().HaveCount(2, "one task per list is a legal shape the collapse must leave alone");
        items.Should().OnlyContain(i => i.ActivityId == survivorId);
    }

    /// <summary>
    /// Three rows onto one key: the survivor already has an item on the list both duplicates' items sit
    /// on. The <c>ActivityProfiles</c> precedent decides the tie — the survivor's own row is the one the
    /// user chose to keep, so it is the row that stays.
    /// </summary>
    [Fact]
    public async Task Merge_WhenTheSurvivorAlreadyHasAnItemOnThatList_ItsOwnItemWins()
    {
        await using var db = CreateDbContext();
        var survivorId = await SeedActivityAsync(db, "Reading Survivor");
        var duplicateAId = await SeedActivityAsync(db, "Reading Duplicate");
        var duplicateBId = await SeedActivityAsync(db, "reading duplicate");

        var categoryId = await TodoListTestSeedHelper.SeedTodoListCategoryAsync(db, ct: CancellationToken);
        var todoListId = await TodoListTestSeedHelper.SeedTodoListAsync(db, categoryId: categoryId, ct: CancellationToken);
        var priorityId = await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, 350, ct: CancellationToken);

        // Seeded first, so "the survivor's item wins" cannot be confused with "the lowest id wins".
        var survivorItemId = await TodoListTestSeedHelper.SeedTodoListItemAsync(db, survivorId, priorityId, todoListId, ct: CancellationToken);
        await TodoListTestSeedHelper.SeedTodoListItemAsync(db, duplicateAId, priorityId, todoListId, ct: CancellationToken);
        await TodoListTestSeedHelper.SeedTodoListItemAsync(db, duplicateBId, priorityId, todoListId, ct: CancellationToken);

        var response = await MergeAsync(survivorId, duplicateAId, duplicateBId);
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "three rows collapsing onto one key is still a merge the user must be able to perform: {0}",
            await response.Content.ReadAsStringAsync(CancellationToken));

        await using var assertDb = CreateDbContext();
        var items = await assertDb.Set<TodoListItem>().Where(i => i.TodoListId == todoListId).ToListAsync(CancellationToken);

        items.Should().ContainSingle();
        items[0].Id.Should().Be(survivorItemId, "the survivor's own row wins, as it does for the activity profiles");
    }

    /// <summary>
    /// The three activity profiles are unique per activity, so two of them cannot both land on the
    /// survivor. The survivor's own profile wins and the merged one is deleted.
    /// </summary>
    [Fact]
    public async Task Merge_WhenBothCarryAProfile_TheSurvivorsProfileWins()
    {
        await using var db = CreateDbContext();
        var survivorId = await SeedActivityAsync(db, "Profiled Survivor");
        var duplicateId = await SeedActivityAsync(db, "Profiled Duplicate");

        var survivorProfileId = await SeedBacklogProfileAsync(db, survivorId, durationMinutes: 30);
        var duplicateProfileId = await SeedBacklogProfileAsync(db, duplicateId, durationMinutes: 90);

        var response = await MergeAsync(survivorId, duplicateId);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var assertDb = CreateDbContext();
        var profiles = await assertDb.Set<ActivityBacklogProfile>()
            .Where(p => p.ActivityId == survivorId)
            .ToListAsync(CancellationToken);

        profiles.Should().ContainSingle("ActivityBacklogProfile.ActivityId is unique — two cannot survive");
        profiles[0].Id.Should().Be(survivorProfileId, "the survivor is the row the user chose to keep");
        profiles[0].DurationMinutes.Should().Be(30);

        (await assertDb.Set<ActivityBacklogProfile>().AnyAsync(p => p.Id == duplicateProfileId, CancellationToken))
            .Should().BeFalse();
    }

    /// <summary>
    /// The other direction: a bare survivor inherits the duplicate's profile rather than discarding it.
    /// Folding a fully-described duplicate into an empty survivor should keep the description.
    /// </summary>
    [Fact]
    public async Task Merge_WhenOnlyTheMergedActivityHasAProfile_ItIsRepointed()
    {
        await using var db = CreateDbContext();
        var survivorId = await SeedActivityAsync(db, "Bare Survivor");
        var duplicateId = await SeedActivityAsync(db, "Described Duplicate");

        var profileId = await SeedBacklogProfileAsync(db, duplicateId, durationMinutes: 45);

        var response = await MergeAsync(survivorId, duplicateId);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var assertDb = CreateDbContext();
        var profile = await assertDb.Set<ActivityBacklogProfile>().FirstOrDefaultAsync(p => p.Id == profileId, CancellationToken);

        profile.Should().NotBeNull();
        profile!.ActivityId.Should().Be(survivorId);
        profile.DurationMinutes.Should().Be(45);
    }

    /// <summary>
    /// Two pattern rules pointing at two duplicates must <b>both</b> survive the merge, repointed onto
    /// the survivor. A mapping is a rule, not a record: destroying one silently stops attributing every
    /// future window that matched it.
    /// </summary>
    /// <remarks>
    /// This used to keep one and delete the rest, and had to — <c>ActivityId</c> carried a unique index
    /// because both configurations said <c>HasOne(...).WithOne()</c>. That 1:1 was a slip rather than a
    /// domain rule (the pattern is the key, and it has its own composite unique index per user), and the
    /// same slip stopped a user mapping both <c>code.exe</c> and <c>rider64.exe</c> onto "Programming".
    /// With the FKs now many-to-one there is nothing to collide, so merge no longer destroys a rule the
    /// user wrote.
    /// </remarks>
    [Fact]
    public async Task Merge_WhenBothCarryATrackingMapping_RepointsBothOntoTheSurvivor()
    {
        await using var db = CreateDbContext();
        var survivorId = await SeedActivityAsync(db, "Mapped Survivor");
        var duplicateId = await SeedActivityAsync(db, "Mapped Duplicate");

        var survivorMapping = new TrackerDesktopMappingByPattern
        {
            UserId = UserId,
            ProcessName = $"survivor-{Guid.NewGuid():N}.exe",
            ProcessNameMatchType = PatternMatchType.Exact,
            IsActive = true,
            ActivityId = survivorId
        };
        var duplicateMapping = new TrackerDesktopMappingByPattern
        {
            UserId = UserId,
            ProcessName = $"duplicate-{Guid.NewGuid():N}.exe",
            ProcessNameMatchType = PatternMatchType.Exact,
            IsActive = true,
            ActivityId = duplicateId
        };
        db.Set<TrackerDesktopMappingByPattern>().AddRange(survivorMapping, duplicateMapping);
        await db.SaveChangesAsync(CancellationToken);

        var response = await MergeAsync(survivorId, duplicateId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var assertDb = CreateDbContext();
        var remaining = await assertDb.Set<TrackerDesktopMappingByPattern>()
            .Where(m => m.ActivityId == survivorId)
            .Select(m => m.Id)
            .ToListAsync(CancellationToken);

        remaining.Should().BeEquivalentTo([survivorMapping.Id, duplicateMapping.Id],
            "both pattern rules are the user's, and an activity may be the target of any number of them");
    }

    /// <summary>
    /// `CQ-2` — <c>RoutineTodoList</c> is unique on <c>(UserId, TimePeriodId, ActivityId)</c>, so two
    /// routines in the <b>same</b> period cannot both end up on the survivor. The survivor's own routine
    /// wins and the duplicate's row is deleted.
    /// </summary>
    /// <remarks>
    /// Left unhandled this is not a routines-shaped failure: the merge is one transaction fanning out
    /// across six slices, so the index rejects the save and the <em>whole</em> merge fails on an
    /// irreversible action the user already confirmed. Asserting on the surviving row's <c>Streak</c>
    /// rather than on the count, because "one routine survived" is also what a merge that kept the wrong
    /// one looks like — and a streak is the thing no re-created activity brings back.
    /// </remarks>
    [Fact]
    public async Task Merge_TwoRoutinesInTheSamePeriod_ResolvesTheCollision()
    {
        await using var db = CreateDbContext();
        var survivorId = await SeedActivityAsync(db, "Routine Survivor");
        var duplicateId = await SeedActivityAsync(db, "Routine Duplicate");

        var periodId = await TodoListTestSeedHelper.SeedRoutineTimePeriodAsync(db, ct: CancellationToken);
        var survivorRoutineId = await TodoListTestSeedHelper.SeedRoutineTodoListAsync(
            db, survivorId, periodId, streak: 7, ct: CancellationToken);
        var duplicateRoutineId = await TodoListTestSeedHelper.SeedRoutineTodoListAsync(
            db, duplicateId, periodId, streak: 2, ct: CancellationToken);

        var response = await MergeAsync(survivorId, duplicateId);
        response.StatusCode.Should().Be(HttpStatusCode.OK, "the whole merge must not fail on a unique index");

        await using var assertDb = CreateDbContext();
        var routines = await assertDb.Set<RoutineTodoList>()
            .Where(r => r.TimePeriodId == periodId)
            .ToListAsync(CancellationToken);

        routines.Should().ContainSingle("(UserId, TimePeriodId, ActivityId) is unique — two cannot survive in one period");
        routines[0].Id.Should().Be(survivorRoutineId, "the survivor is the row the user chose to keep");
        routines[0].ActivityId.Should().Be(survivorId);
        routines[0].Streak.Should().Be(7, "the survivor's streak is the one the user has been looking at");

        (await assertDb.Set<RoutineTodoList>().AnyAsync(r => r.Id == duplicateRoutineId, CancellationToken))
            .Should().BeFalse();
    }

    /// <summary>
    /// The control the collision rule must not swallow: two routines on the two activities in
    /// <em>different</em> periods are two different habits, and both survive with their streaks intact.
    /// This is the case the seam's remark describes.
    /// </summary>
    [Fact]
    public async Task Merge_TwoRoutinesInDifferentPeriods_KeepsBothAndRepointsBoth()
    {
        await using var db = CreateDbContext();
        var survivorId = await SeedActivityAsync(db, "Two-Period Survivor");
        var duplicateId = await SeedActivityAsync(db, "Two-Period Duplicate");

        // (UserId, LengthInDays) is unique on a period, so the second one has to be a different length.
        var dailyPeriodId = await TodoListTestSeedHelper.SeedRoutineTimePeriodAsync(db, lengthInDays: 1, resetAnchorDay: 0, ct: CancellationToken);
        var weeklyPeriodId = await TodoListTestSeedHelper.SeedRoutineTimePeriodAsync(db, lengthInDays: 7, ct: CancellationToken);

        var survivorRoutineId = await TodoListTestSeedHelper.SeedRoutineTodoListAsync(
            db, survivorId, dailyPeriodId, streak: 7, ct: CancellationToken);
        var duplicateRoutineId = await TodoListTestSeedHelper.SeedRoutineTodoListAsync(
            db, duplicateId, weeklyPeriodId, streak: 2, ct: CancellationToken);

        var response = await MergeAsync(survivorId, duplicateId);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var assertDb = CreateDbContext();
        var survivorRoutine = await assertDb.Set<RoutineTodoList>().FirstAsync(r => r.Id == survivorRoutineId, CancellationToken);
        var duplicateRoutine = await assertDb.Set<RoutineTodoList>().FirstAsync(r => r.Id == duplicateRoutineId, CancellationToken);

        survivorRoutine.ActivityId.Should().Be(survivorId);
        survivorRoutine.Streak.Should().Be(7);
        duplicateRoutine.ActivityId.Should().Be(survivorId, "nothing collides across periods, so it is repointed rather than dropped");
        duplicateRoutine.Streak.Should().Be(2, "merging says the activities were the same thing, not that the habits were");
    }

    // ---- the permitted-but-worth-stating cases ---------------------------------------------------

    /// <summary>
    /// Cross-role and cross-category merges are permitted and the survivor's placement wins. Rejecting
    /// them would block the most common real duplicate: one activity quick-created from two dialogs that
    /// defaulted to different roles.
    /// </summary>
    [Fact]
    public async Task Merge_AcrossRolesAndCategories_IsAllowed_AndTheSurvivorWins()
    {
        await using var db = CreateDbContext();

        var survivorRole = new ActivityRole { UserId = UserId, Name = $"Survivor Role {Guid.NewGuid():N}", Color = "#111111" };
        var duplicateRole = new ActivityRole { UserId = UserId, Name = $"Duplicate Role {Guid.NewGuid():N}", Color = "#222222" };
        var survivorCategory = new ActivityCategory { UserId = UserId, Name = $"Survivor Cat {Guid.NewGuid():N}", Color = "#333333" };
        var duplicateCategory = new ActivityCategory { UserId = UserId, Name = $"Duplicate Cat {Guid.NewGuid():N}", Color = "#444444" };
        db.Set<ActivityRole>().AddRange(survivorRole, duplicateRole);
        db.Set<ActivityCategory>().AddRange(survivorCategory, duplicateCategory);
        await db.SaveChangesAsync(CancellationToken);

        var survivorId = await SeedActivityAsync(db, "Cross Survivor", roleId: survivorRole.Id, categoryId: survivorCategory.Id);
        var duplicateId = await SeedActivityAsync(db, "Cross Duplicate", roleId: duplicateRole.Id, categoryId: duplicateCategory.Id);

        var response = await MergeAsync(survivorId, duplicateId);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var assertDb = CreateDbContext();
        var survivor = await assertDb.Set<Activity>().FirstAsync(a => a.Id == survivorId, CancellationToken);

        survivor.RoleId.Should().Be(survivorRole.Id, "the merged row's placement is discarded along with the row");
        survivor.CategoryId.Should().Be(survivorCategory.Id);
    }

    /// <summary>
    /// An archived survivor is allowed. The user picked it from the All view, so history landing on an
    /// activity that is invisible in every picker is what they asked for — and it is one restore away.
    /// </summary>
    [Fact]
    public async Task Merge_IntoAnArchivedSurvivor_IsAllowed_AndLeavesItArchived()
    {
        await using var db = CreateDbContext();
        var survivorId = await SeedActivityAsync(db, "Archived Survivor", isArchived: true);
        var duplicateId = await SeedActivityAsync(db, "Active Duplicate");

        db.Set<TimerPreset>().Add(new TimerPreset { UserId = UserId, Duration = 300, ActivityId = duplicateId });
        await db.SaveChangesAsync(CancellationToken);

        var response = await MergeAsync(survivorId, duplicateId);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var assertDb = CreateDbContext();
        var survivor = await assertDb.Set<Activity>().IgnoreQueryFilters([AppQueryFilters.ActivityArchived]).FirstAsync(a => a.Id == survivorId, CancellationToken);

        survivor.IsArchived.Should().BeTrue("merging is not a reason to un-retire the target");
        (await assertDb.Set<TimerPreset>().AnyAsync(p => p.ActivityId == survivorId, CancellationToken)).Should().BeTrue();
    }

    /// <summary>
    /// `SEC-4` — the same archived survivor, but carrying a profile of its own. The collision check in
    /// <c>ActivityProfileActivityReferenceSource</c> reads the survivor's profile through a table that
    /// mirrors <c>AppQueryFilters.ActivityArchived</c>, so unlifted it sees nothing, concludes the
    /// survivor is bare, and repoints the merged profile onto an <c>ActivityId</c> that is <b>unique</b>
    /// — the index rejects it, the transaction rolls back, and the user gets an error on an irreversible
    /// action they already confirmed. The archived case has to resolve exactly like the active one.
    /// </summary>
    [Fact]
    public async Task Merge_IntoAnArchivedSurvivorThatAlreadyHasAProfile_KeepsTheSurvivorsProfile()
    {
        await using var db = CreateDbContext();
        var survivorId = await SeedActivityAsync(db, "Archived Profiled Survivor", isArchived: true);
        var duplicateId = await SeedActivityAsync(db, "Duplicate For Archived Profile");

        var survivorProfileId = await SeedBacklogProfileAsync(db, survivorId, durationMinutes: 20);
        var duplicateProfileId = await SeedBacklogProfileAsync(db, duplicateId, durationMinutes: 80);

        var response = await MergeAsync(survivorId, duplicateId);
        response.StatusCode.Should().Be(HttpStatusCode.OK, "the whole merge must not fail on a unique index");

        await using var assertDb = CreateDbContext();
        var profiles = await assertDb.Set<ActivityBacklogProfile>()
            .IgnoreQueryFilters([AppQueryFilters.ActivityArchived])
            .Where(p => p.ActivityId == survivorId)
            .ToListAsync(CancellationToken);

        profiles.Should().ContainSingle("ActivityBacklogProfile.ActivityId is unique — two cannot survive");
        profiles[0].Id.Should().Be(survivorProfileId, "archived or not, the survivor's own profile wins");
        profiles[0].DurationMinutes.Should().Be(20);

        (await assertDb.Set<ActivityBacklogProfile>()
            .IgnoreQueryFilters([AppQueryFilters.ActivityArchived])
            .AnyAsync(p => p.Id == duplicateProfileId, CancellationToken))
            .Should().BeFalse();
    }

    /// <summary>
    /// An archived activity may be merged <em>away</em>, not just merged into — `SEC-3`. The dialog draws
    /// its candidates from the grid's All view, which is the whole reason <c>IsArchived</c> is tri-state,
    /// so an archived duplicate is selectable; leaving the archival filter on the merged query made the
    /// count fall short and 404'd the entire merge over an id the UI itself offered.
    /// </summary>
    [Fact]
    public async Task Merge_AnArchivedActivity_CanBeMergedAway()
    {
        await using var db = CreateDbContext();
        var survivorId = await SeedActivityAsync(db, "Active Survivor");
        var archivedDuplicateId = await SeedActivityAsync(db, "Archived Duplicate", isArchived: true);

        var history = new ActivityHistory
        {
            UserId = UserId,
            ActivityId = archivedDuplicateId,
            StartTimestamp = DateTime.UtcNow.AddHours(-2),
            Length = new IntTime(0, 30),
            EndTimestamp = DateTime.UtcNow.AddMinutes(-90)
        };
        db.Set<ActivityHistory>().Add(history);
        await db.SaveChangesAsync(CancellationToken);

        var response = await MergeAsync(survivorId, archivedDuplicateId);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<MergeResult>(JsonOpts, CancellationToken))!.MergedCount.Should().Be(1);

        await using var assertDb = CreateDbContext();
        (await assertDb.Set<ActivityHistory>().IgnoreQueryFilters().FirstAsync(h => h.Id == history.Id, CancellationToken))
            .ActivityId.Should().Be(survivorId, "the archived duplicate's history must land on the survivor");
        (await assertDb.Set<Activity>().IgnoreQueryFilters().AnyAsync(a => a.Id == archivedDuplicateId, CancellationToken))
            .Should().BeFalse("merged activities are deleted, archived or not");
    }

    /// <summary>
    /// A mixed set merges as one unit. Before `SEC-3` the single archived id 404'd the whole call, so the
    /// active duplicate was not merged either and the user was told "not found" about a set on screen.
    /// </summary>
    [Fact]
    public async Task Merge_MixedActiveAndArchivedMergedIds_MergesAsAUnit()
    {
        await using var db = CreateDbContext();
        var survivorId = await SeedActivityAsync(db, "Mixed Survivor");
        var activeDuplicateId = await SeedActivityAsync(db, "Mixed Active Duplicate");
        var archivedDuplicateId = await SeedActivityAsync(db, "Mixed Archived Duplicate", isArchived: true);

        var activePreset = new TimerPreset { UserId = UserId, Duration = 600, ActivityId = activeDuplicateId };
        var archivedPreset = new TimerPreset { UserId = UserId, Duration = 900, ActivityId = archivedDuplicateId };
        db.Set<TimerPreset>().AddRange(activePreset, archivedPreset);
        await db.SaveChangesAsync(CancellationToken);

        var response = await MergeAsync(survivorId, activeDuplicateId, archivedDuplicateId);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<MergeResult>(JsonOpts, CancellationToken))!.MergedCount.Should().Be(2);

        await using var assertDb = CreateDbContext();
        var presets = await assertDb.Set<TimerPreset>()
            .Where(p => p.Id == activePreset.Id || p.Id == archivedPreset.Id).ToListAsync(CancellationToken);

        presets.Should().HaveCount(2).And.OnlyContain(p => p.ActivityId == survivorId, "both halves move or neither does");
        (await assertDb.Set<Activity>().IgnoreQueryFilters()
            .CountAsync(a => a.Id == activeDuplicateId || a.Id == archivedDuplicateId, CancellationToken))
            .Should().Be(0);
    }

    /// <summary>
    /// Archived into archived — the case that proves the filter is lifted on <em>both</em> queries rather
    /// than swapped from one to the other.
    /// </summary>
    [Fact]
    public async Task Merge_ArchivedIntoArchived_IsAllowed_AndLeavesTheSurvivorArchived()
    {
        await using var db = CreateDbContext();
        var survivorId = await SeedActivityAsync(db, "Retired Survivor", isArchived: true);
        var archivedDuplicateId = await SeedActivityAsync(db, "Retired Duplicate", isArchived: true);

        var preset = new TimerPreset { UserId = UserId, Duration = 1200, ActivityId = archivedDuplicateId };
        db.Set<TimerPreset>().Add(preset);
        await db.SaveChangesAsync(CancellationToken);

        var response = await MergeAsync(survivorId, archivedDuplicateId);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var assertDb = CreateDbContext();
        var survivor = await assertDb.Set<Activity>().IgnoreQueryFilters([AppQueryFilters.ActivityArchived])
            .FirstAsync(a => a.Id == survivorId, CancellationToken);

        survivor.IsArchived.Should().BeTrue("merging is not a restore");
        (await assertDb.Set<TimerPreset>().FirstAsync(p => p.Id == preset.Id, CancellationToken))
            .ActivityId.Should().Be(survivorId);
        (await assertDb.Set<Activity>().IgnoreQueryFilters().AnyAsync(a => a.Id == archivedDuplicateId, CancellationToken))
            .Should().BeFalse();
    }

    /// <summary>
    /// The regression pin that matters most here: lifting the archival filter must not lift the per-user
    /// one with it. A foreign archived id stays a 404.
    /// </summary>
    [Fact]
    public async Task Merge_AnotherUsersArchivedActivity_StillReturns404()
    {
        await using var db = CreateDbContext();
        var survivorId = await SeedActivityAsync(db, "Mine, Again");

        var otherUserId = await ActivityMergeTestSupport.SecondUserAsync(Fixture, $"merge-archived-{Guid.NewGuid():N}@test.com");
        var foreignRole = new ActivityRole { UserId = otherUserId, Name = $"Foreign Role {Guid.NewGuid():N}", Color = "#445566" };
        db.Set<ActivityRole>().Add(foreignRole);
        await db.SaveChangesAsync(CancellationToken);
        var foreign = new Activity { UserId = otherUserId, Name = "Theirs, Archived", RoleId = foreignRole.Id, IsArchived = true };
        db.Set<Activity>().Add(foreign);
        await db.SaveChangesAsync(CancellationToken);

        (await MergeAsync(survivorId, foreign.Id)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using var assertDb = CreateDbContext();
        (await assertDb.Set<Activity>().IgnoreQueryFilters().AnyAsync(a => a.Id == foreign.Id, CancellationToken))
            .Should().BeTrue("the per-user filter must stay on when the archival one is lifted");
    }

    // ---- failure responses -----------------------------------------------------------------------

    [Fact]
    public async Task Merge_UnknownSurvivor_Returns404()
    {
        await using var db = CreateDbContext();
        var duplicateId = await SeedActivityAsync(db, "Orphan Duplicate");

        (await MergeAsync(987654321, duplicateId)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// One unknown id fails the <em>whole</em> merge. There is no partial: a half-applied merge is the
    /// state the user has no way to see or to finish.
    /// </summary>
    [Fact]
    public async Task Merge_UnknownMergedId_FailsTheWholeMerge()
    {
        await using var db = CreateDbContext();
        var survivorId = await SeedActivityAsync(db, "Intact Survivor");
        var realDuplicateId = await SeedActivityAsync(db, "Real Duplicate");

        db.Set<TimerPreset>().Add(new TimerPreset { UserId = UserId, Duration = 60, ActivityId = realDuplicateId });
        await db.SaveChangesAsync(CancellationToken);

        var response = await MergeAsync(survivorId, realDuplicateId, 987654321);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using var assertDb = CreateDbContext();
        (await assertDb.Set<Activity>().AnyAsync(a => a.Id == realDuplicateId, CancellationToken))
            .Should().BeTrue("nothing may be applied when any id is rejected");
        (await assertDb.Set<TimerPreset>().FirstAsync(p => p.ActivityId != null, CancellationToken))
            .ActivityId.Should().Be(realDuplicateId, "the valid half must not have been repointed either");
    }

    [Fact]
    public async Task Merge_EmptyMergedIds_Returns400()
    {
        await using var db = CreateDbContext();
        var survivorId = await SeedActivityAsync(db, "Nothing To Merge");

        (await MergeAsync(survivorId)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// The client strips the survivor from the list, so this is a bug rather than a user state — and it
    /// must be rejected rather than ignored: folding an activity into itself would repoint its own rows
    /// onto itself and then delete it, cascading every one of them away.
    /// </summary>
    [Fact]
    public async Task Merge_SurvivorInsideMergedIds_Returns400()
    {
        await using var db = CreateDbContext();
        var survivorId = await SeedActivityAsync(db, "Self Merge");

        var response = await MergeAsync(survivorId, survivorId);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await using var assertDb = CreateDbContext();
        (await assertDb.Set<Activity>().AnyAsync(a => a.Id == survivorId, CancellationToken)).Should().BeTrue();
    }

    /// <summary>
    /// A cross-user id answers 404, not 403 — the response must not confirm that the row exists.
    /// </summary>
    [Fact]
    public async Task Merge_AcrossUsers_Returns404_AndTouchesNothing()
    {
        await using var db = CreateDbContext();
        var survivorId = await SeedActivityAsync(db, "Mine");

        var otherUserId = await ActivityMergeTestSupport.SecondUserAsync(Fixture, $"merge-{Guid.NewGuid():N}@test.com");
        var foreignRole = new ActivityRole { UserId = otherUserId, Name = $"Foreign Role {Guid.NewGuid():N}", Color = "#112233" };
        db.Set<ActivityRole>().Add(foreignRole);
        await db.SaveChangesAsync(CancellationToken);
        var foreign = new Activity { UserId = otherUserId, Name = "Theirs", RoleId = foreignRole.Id };
        db.Set<Activity>().Add(foreign);
        await db.SaveChangesAsync(CancellationToken);

        var response = await MergeAsync(survivorId, foreign.Id);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using var assertDb = CreateDbContext();
        (await assertDb.Set<Activity>().IgnoreQueryFilters().AnyAsync(a => a.Id == foreign.Id, CancellationToken))
            .Should().BeTrue("another account's activity must survive untouched");
    }

    // ---- seeding ---------------------------------------------------------------------------------

    private async Task<long> SeedBacklogProfileAsync(DbContext db, long activityId, int durationMinutes)
    {
        var locationTypeId = await SeedLookupAsync<ActivityLocationType>(db);
        var weatherId = await SeedLookupAsync<ActivityWeatherDependency>(db);
        var costTierId = await SeedLookupAsync<ActivityExpectedCostTier>(db);

        var profile = new ActivityBacklogProfile
        {
            ActivityId = activityId,
            LocationTypeId = locationTypeId,
            WeatherDependencyId = weatherId,
            EnergyLevel = EnergyLevel.Medium,
            MinParticipants = 1,
            ExpectedCostTierId = costTierId,
            DurationMinutes = durationMinutes,
            IsRepeatable = true
        };
        db.Set<ActivityBacklogProfile>().Add(profile);
        await db.SaveChangesAsync(CancellationToken);
        return profile.Id;
    }

    private async Task<long> SeedLookupAsync<TLookup>(DbContext db) where TLookup : BaseLookupWithUser, new()
    {
        var lookup = new TLookup { UserId = UserId, Text = $"{typeof(TLookup).Name}-{Guid.NewGuid():N}" };
        db.Set<TLookup>().Add(lookup);
        await db.SaveChangesAsync(CancellationToken);
        return lookup.Id;
    }
}