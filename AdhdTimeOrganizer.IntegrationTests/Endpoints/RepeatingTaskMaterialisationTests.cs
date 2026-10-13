using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.Core.domain.model.@enum;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.IntegrationTests.Reminders;
using AdhdTimeOrganizer.Planning.application.dto.@enum;
using AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// FO6 — an auto-placing repeating rule puts its own occurrences on today's and tomorrow's plan, from the
/// day-plan read (<c>GET /calendar/day-plan/{date}</c>) and from the rule's own create/update.
/// <para>
/// A GET that writes fails <b>silently</b> in both directions: a missed placement is an empty day, and a
/// resurrected one is a block the user already threw away. So every case asserts on the stored
/// <c>planner_task</c> rows, not on the response.
/// </para>
/// <para>The fixture user is on UTC, so the user's today is UTC's.</para>
/// </summary>
[Collection("Postgres")]
public class RepeatingTaskMaterialisationTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    private static readonly string[] EveryDay =
        ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
    private static DateOnly Tomorrow => Today.AddDays(1);

    [Fact(DisplayName = "FO6: reading today places today and tomorrow, and nothing further out")]
    public async Task Read_PlacesTodayAndTomorrowOnly()
    {
        var ruleId = await SeedRuleAsync();

        var plan = await GetDayPlanAsync(Today);
        // A read of the day after tomorrow is outside the window and must not extend it.
        await GetDayPlanAsync(Today.AddDays(2));

        var occurrences = await OccurrencesAsync(ruleId);
        occurrences.Select(o => o.Date).Should().BeEquivalentTo([Today, Tomorrow]);
        occurrences.Should().OnlyContain(o => o.Task.Status == PlannerTaskStatus.NotStarted
                                              && o.Task.StartTime == new TimeOnly(18, 0)
                                              && o.Task.PostponeCount == 0
                                              && o.Task.OriginalDate == null);

        plan.Tasks.Should().ContainSingle(t => t.SourceRepeatingTaskId == ruleId,
            "the read returns the day including what it just placed");
        (await RuleAsync(ruleId)).MaterialisedThrough.Should().Be(Tomorrow);
    }

    [Fact(DisplayName = "FO6: a past day's read places nothing")]
    public async Task Read_OfAPastDay_PlacesNothing()
    {
        var ruleId = await SeedRuleAsync();

        await GetDayPlanAsync(Today.AddDays(-1));

        (await OccurrencesAsync(ruleId)).Should().BeEmpty();
        (await RuleAsync(ruleId)).MaterialisedThrough.Should().BeNull();
    }

    [Fact(DisplayName = "FO6: reading again, either day, adds no duplicate")]
    public async Task Reread_AddsNoDuplicate()
    {
        var ruleId = await SeedRuleAsync();

        await GetDayPlanAsync(Today);
        await GetDayPlanAsync(Today);
        await GetDayPlanAsync(Tomorrow);

        (await OccurrencesAsync(ruleId)).Should().HaveCount(2);
    }

    [Fact(DisplayName = "FO6: reading tomorrow first still places today")]
    public async Task ReadingTomorrowFirst_StillPlacesToday()
    {
        var ruleId = await SeedRuleAsync();

        await GetDayPlanAsync(Tomorrow);

        (await OccurrencesAsync(ruleId)).Select(o => o.Date).Should().BeEquivalentTo([Today, Tomorrow]);
    }

    /// <summary>
    /// The day planner opens a day through by-Date, not day-plan, so a user who goes to the planner before home
    /// must still find the blocks — and a day with no row yet answers 200 rather than the route's usual 404.
    /// </summary>
    [Fact(DisplayName = "FO6: the planner's by-Date read places today and tomorrow too")]
    public async Task ByDateRead_PlacesTodayAndTomorrow()
    {
        var ruleId = await SeedRuleAsync();

        var response = await CreateClient().GetAsync($"api/calendar/by-Date/{Tomorrow:dd-MM-yyyy}", CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(CancellationToken));
        // Outside the window: answers as before and extends nothing.
        await CreateClient().GetAsync($"api/calendar/by-Date/{Today.AddDays(2):dd-MM-yyyy}", CancellationToken);

        (await OccurrencesAsync(ruleId)).Select(o => o.Date).Should().BeEquivalentTo([Today, Tomorrow]);
    }

    [Fact(DisplayName = "FO6: a deleted occurrence never comes back")]
    public async Task DeletedOccurrence_StaysDeleted()
    {
        var ruleId = await SeedRuleAsync();
        await GetDayPlanAsync(Today);
        var todays = (await OccurrencesAsync(ruleId)).Single(o => o.Date == Today).Task;

        var delete = await CreateClient().DeleteAsync($"api/planner-task/{todays.Id}", CancellationToken);
        delete.IsSuccessStatusCode.Should().BeTrue(await delete.Content.ReadAsStringAsync(CancellationToken));

        await GetDayPlanAsync(Today);
        await GetDayPlanAsync(Tomorrow);

        (await OccurrencesAsync(ruleId)).Select(o => o.Date).Should().BeEquivalentTo([Tomorrow]);
        (await RuleAsync(ruleId)).Should().NotBeNull("deleting an occurrence never touches the rule");
    }

    [Fact(DisplayName = "FO6: a skipped occurrence stays skipped, and is not joined by a fresh one")]
    public async Task SkippedOccurrence_StaysSkipped()
    {
        var ruleId = await SeedRuleAsync();
        await GetDayPlanAsync(Today);
        var todays = (await OccurrencesAsync(ruleId)).Single(o => o.Date == Today).Task;

        var skip = await CreateClient().PatchAsJsonAsync($"api/planner-task/{todays.Id}/status",
            new { Status = "Cancelled", SkipReasonKey = "noTime" }, JsonOpts, CancellationToken);
        skip.StatusCode.Should().Be(HttpStatusCode.NoContent, await skip.Content.ReadAsStringAsync(CancellationToken));

        await GetDayPlanAsync(Today);

        var onToday = (await OccurrencesAsync(ruleId)).Where(o => o.Date == Today).ToList();
        onToday.Should().ContainSingle().Which.Task.Status.Should().Be(PlannerTaskStatus.Cancelled);
    }

    [Fact(DisplayName = "FO6: an away day gets no occurrence, and un-marking it later does not bring one")]
    public async Task AwayDay_IsPassedOver()
    {
        var ruleId = await SeedRuleAsync();
        // Set rather than seeded with the type: the helper hands back an existing row unchanged.
        await SetDayTypeAsync(Tomorrow, DayType.Vacation);

        await GetDayPlanAsync(Today);
        (await OccurrencesAsync(ruleId)).Select(o => o.Date).Should().BeEquivalentTo([Today]);

        await SetDayTypeAsync(Tomorrow, DayType.Workday);

        await GetDayPlanAsync(Tomorrow);
        (await OccurrencesAsync(ruleId)).Select(o => o.Date).Should().BeEquivalentTo([Today],
            "the away day was decided when it was first read");
    }

    [Fact(DisplayName = "FO6: an inactive rule, or one that does not auto-place, places nothing")]
    public async Task OnlyActiveAutoPlacingRules_Place()
    {
        var paused = await SeedRuleAsync(isActive: false);
        var suggestOnly = await SeedRuleAsync(autoPlace: false);

        await GetDayPlanAsync(Today);

        (await OccurrencesAsync(paused)).Should().BeEmpty();
        (await OccurrencesAsync(suggestOnly)).Should().BeEmpty();
    }

    [Fact(DisplayName = "FO6: a rule that does not fall on the day places nothing there")]
    public async Task RuleNotOnTheDay_PlacesNothingThere()
    {
        var ruleId = await SeedRuleAsync(days: [Tomorrow.DayOfWeek.ToString()]);

        await GetDayPlanAsync(Today);

        (await OccurrencesAsync(ruleId)).Select(o => o.Date).Should().BeEquivalentTo([Tomorrow]);
    }

    /// <summary>
    /// Planner lanes: two blocks of a kind may share a minute, drawn side by side. So an occurrence that would be
    /// only the second block anywhere goes on whole, in every mode — before the lanes, any overlap skipped it
    /// (<c>Ignore</c>) or carved it (<c>MergeIgnore</c>).
    /// </summary>
    [Theory(DisplayName = "FO6: an occurrence that would be a second block is placed whole beside it, in every mode")]
    [InlineData(null)]
    [InlineData(ApplyTemplateConflictResolutionEnum.Ignore)]
    [InlineData(ApplyTemplateConflictResolutionEnum.Overwrite)]
    [InlineData(ApplyTemplateConflictResolutionEnum.MergeIgnore)]
    [InlineData(ApplyTemplateConflictResolutionEnum.MergeOverwrite)]
    public async Task BesideOneBlock_IsPlacedWhole(ApplyTemplateConflictResolutionEnum? mode)
    {
        await SetConflictModeAsync(mode);
        var ruleId = await SeedRuleAsync();
        var blockerId = await SeedBlockAsync(Today, new TimeOnly(18, 30), new TimeOnly(19, 30));

        await GetDayPlanAsync(Today);

        (await OccurrencesAsync(ruleId)).Where(o => o.Date == Today).Should().ContainSingle()
            .Which.Task.Should().Match<PlannerTask>(t => t.StartTime == new TimeOnly(18, 0) && t.EndTime == new TimeOnly(19, 0));
        await ShouldBeUntouchedAsync(blockerId, new TimeOnly(18, 30), new TimeOnly(19, 30));
    }

    /// <summary>
    /// Both lanes are taken for 18:30–19:00, so the occurrence would be a third block there. <c>Ignore</c> (and
    /// <c>Overwrite</c>, read as <c>Ignore</c> because this runs from a GET) skips it; neither block is touched.
    /// </summary>
    [Theory(DisplayName = "FO6: under Ignore, an occurrence that would be a third block is not placed")]
    [InlineData(null)]
    [InlineData(ApplyTemplateConflictResolutionEnum.Ignore)]
    [InlineData(ApplyTemplateConflictResolutionEnum.Overwrite)]
    public async Task ThirdBlock_UnderIgnore_IsSkipped(ApplyTemplateConflictResolutionEnum? mode)
    {
        await SetConflictModeAsync(mode);
        var ruleId = await SeedRuleAsync();
        var firstId = await SeedBlockAsync(Today, new TimeOnly(18, 0), new TimeOnly(19, 0));
        var secondId = await SeedBlockAsync(Today, new TimeOnly(18, 30), new TimeOnly(19, 30));

        await GetDayPlanAsync(Today);

        (await OccurrencesAsync(ruleId)).Select(o => o.Date).Should().BeEquivalentTo([Tomorrow],
            "today's would be a third block; tomorrow is free");
        await ShouldBeUntouchedAsync(firstId, new TimeOnly(18, 0), new TimeOnly(19, 0));
        await ShouldBeUntouchedAsync(secondId, new TimeOnly(18, 30), new TimeOnly(19, 30));
        (await RuleAsync(ruleId)).MaterialisedThrough.Should().Be(Tomorrow, "a skipped day is still decided");
    }

    /// <summary>
    /// Both lanes are taken only for 18:30–18:45. <c>MergeIgnore</c> (and <c>MergeOverwrite</c>, read as it) cuts
    /// exactly those minutes out, keeping 18:00–18:30 and 18:45–19:00 beside one block each. The old carve around
    /// every overlapping block would have left nothing, since the two blocks between them cover the whole hour.
    /// </summary>
    [Theory(DisplayName = "FO6: under MergeIgnore, a third block is cut to the stretches where it is a second, never the user's")]
    [InlineData(ApplyTemplateConflictResolutionEnum.MergeIgnore)]
    [InlineData(ApplyTemplateConflictResolutionEnum.MergeOverwrite)]
    public async Task ThirdBlock_UnderMergeIgnore_IsCutToItsLanes(ApplyTemplateConflictResolutionEnum mode)
    {
        await SetConflictModeAsync(mode);
        var ruleId = await SeedRuleAsync();
        var earlyId = await SeedBlockAsync(Today, new TimeOnly(18, 0), new TimeOnly(18, 45));
        var lateId = await SeedBlockAsync(Today, new TimeOnly(18, 30), new TimeOnly(19, 30));

        await GetDayPlanAsync(Today);

        var todays = (await OccurrencesAsync(ruleId)).Where(o => o.Date == Today).Select(o => o.Task)
            .OrderBy(t => t.StartTime).Select(t => (t.StartTime, t.EndTime)).ToList();
        todays.Should().Equal(
            (new TimeOnly(18, 0), new TimeOnly(18, 30)),
            (new TimeOnly(18, 45), new TimeOnly(19, 0)));
        await ShouldBeUntouchedAsync(earlyId, new TimeOnly(18, 0), new TimeOnly(18, 45));
        await ShouldBeUntouchedAsync(lateId, new TimeOnly(18, 30), new TimeOnly(19, 30));
        (await OccurrencesAsync(ruleId)).Where(o => o.Date == Tomorrow).Should().ContainSingle(
            "tomorrow is free, so its occurrence is whole");
    }

    /// <summary>
    /// Foreground and background blocks have a lane pair each. Today's two foreground blocks fill the foreground
    /// lanes and leave the background ones empty; tomorrow is the other way round.
    /// </summary>
    [Fact(DisplayName = "FO6: background and foreground blocks are counted separately")]
    public async Task Kinds_AreCountedSeparately()
    {
        var foregroundRule = await SeedRuleAsync();
        var backgroundRule = await SeedRuleAsync(isBackground: true);
        await SeedBlockAsync(Today, new TimeOnly(18, 0), new TimeOnly(19, 0));
        await SeedBlockAsync(Today, new TimeOnly(18, 0), new TimeOnly(19, 0));
        await SeedBlockAsync(Tomorrow, new TimeOnly(18, 0), new TimeOnly(19, 0), isBackground: true);
        await SeedBlockAsync(Tomorrow, new TimeOnly(18, 0), new TimeOnly(19, 0), isBackground: true);

        await GetDayPlanAsync(Today);

        (await OccurrencesAsync(foregroundRule)).Select(o => o.Date).Should().BeEquivalentTo([Tomorrow],
            "today's foreground lanes are full; tomorrow's background blocks are not in its way");
        (await OccurrencesAsync(backgroundRule)).Select(o => o.Date).Should().BeEquivalentTo([Today],
            "today's foreground blocks are not in its way; tomorrow's background lanes are full");
        (await OccurrencesAsync(backgroundRule)).Should().OnlyContain(o => o.Task.IsBackground
                                                                          && o.Task.StartTime == new TimeOnly(18, 0)
                                                                          && o.Task.EndTime == new TimeOnly(19, 0));
    }

    /// <summary>
    /// The occurrences one run places count against each other, in rule order: with a user block at 18:00, the
    /// older rule takes the second lane and the newer would be the third. Tomorrow has no block, so both fit.
    /// </summary>
    [Fact(DisplayName = "FO6: two occurrences and a user block at one minute are three — the newer rule gives way")]
    public async Task TwoOccurrencesAndABlock_InOneRun_AreThree()
    {
        var olderRule = await SeedRuleAsync();
        var newerRule = await SeedRuleAsync();
        var blockerId = await SeedBlockAsync(Today, new TimeOnly(18, 0), new TimeOnly(19, 0));

        await GetDayPlanAsync(Today);

        (await OccurrencesAsync(olderRule)).Select(o => o.Date).Should().BeEquivalentTo([Today, Tomorrow]);
        (await OccurrencesAsync(newerRule)).Select(o => o.Date).Should().BeEquivalentTo([Tomorrow],
            "beside the user's block and the older rule's occurrence, today's would be a third");
        await ShouldBeUntouchedAsync(blockerId, new TimeOnly(18, 0), new TimeOnly(19, 0));
    }

    /// <summary>
    /// The occurrence is placed the moment a day-plan is read, so marking tomorrow as sick leave tonight found the
    /// gym block already there. Only an untouched one goes: a block the user moved is theirs.
    /// </summary>
    [Fact(DisplayName = "FO6: marking a day away after placement withdraws its untouched occurrence, not a moved one")]
    public async Task MarkingAwayAfterPlacement_WithdrawsTheUntouchedOccurrence()
    {
        var untouchedRuleId = await SeedRuleAsync();
        var movedRuleId = await SeedRuleAsync();
        await GetDayPlanAsync(Today);

        // The user moves one of tomorrow's occurrences, which makes it theirs.
        await using (var db = CreateDbContext())
        {
            var moved = await db.Set<PlannerTask>().IgnoreQueryFilters()
                .SingleAsync(t => t.SourceRepeatingTaskId == movedRuleId && t.Calendar.Date == Tomorrow, CancellationToken);
            moved.StartTime = new TimeOnly(20, 0);
            moved.EndTime = new TimeOnly(21, 0);
            await db.SaveChangesAsync(CancellationToken);
        }

        await MarkDayTypeAsync(Tomorrow, "SickDay");

        (await OccurrencesAsync(untouchedRuleId)).Select(o => o.Date).Should().BeEquivalentTo([Today],
            "tomorrow's untouched occurrence is withdrawn; today's, on a day that is not away, stays");
        (await OccurrencesAsync(movedRuleId)).Select(o => o.Date).Should().BeEquivalentTo([Today, Tomorrow],
            "a moved occurrence is the user's block now");

        await MarkDayTypeAsync(Tomorrow, Tomorrow.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? "Weekend" : "Workday");
        await GetDayPlanAsync(Tomorrow);
        (await OccurrencesAsync(untouchedRuleId)).Select(o => o.Date).Should().BeEquivalentTo([Today],
            "the day stays decided, as a day that was away when the rule reached it");
    }

    [Fact(DisplayName = "FO6: another user's auto-placing rule is never materialised by my read")]
    public async Task AnotherUsersRule_IsNeverMaterialised()
    {
        long theirRuleId;
        await using (var db = CreateDbContext())
        {
            await ReminderSeedHelper.EnsureOtherUserAsync(db, CancellationToken);
            theirRuleId = await SeedRuleAsync(userId: ReminderSeedHelper.OtherUserId);
        }

        await GetDayPlanAsync(Today);

        (await OccurrencesAsync(theirRuleId)).Should().BeEmpty();
        await using (var db = CreateDbContext())
        {
            (await db.Set<Calendar>().IgnoreQueryFilters()
                    .AnyAsync(c => c.UserId == ReminderSeedHelper.OtherUserId && c.Date >= Today, CancellationToken))
                .Should().BeFalse("no day of theirs is provisioned either");
        }
        (await RuleAsync(theirRuleId)).MaterialisedThrough.Should().BeNull();
    }

    [Fact(DisplayName = "FO6: creating an auto-placing rule places it straight away")]
    public async Task Create_PlacesStraightAway()
    {
        var (activityId, importanceId) = await SeedReferencesAsync();

        var response = await CreateClient().PostAsJsonAsync("api/repeating-planner-task",
            RuleBody(activityId, importanceId, startHour: 18, autoPlace: true), JsonOpts, CancellationToken);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created, body);
        var ruleId = JsonSerializer.Deserialize<long>(body, JsonOpts);

        (await OccurrencesAsync(ruleId)).Select(o => o.Date).Should().BeEquivalentTo([Today, Tomorrow]);
    }

    [Fact(DisplayName = "FO6: a rule scheduled only for away day types cannot auto-place")]
    public async Task Validator_RejectsAnAwayOnlyAutoPlacingRule()
    {
        var (activityId, importanceId) = await SeedReferencesAsync();

        var response = await CreateClient().PostAsJsonAsync("api/repeating-planner-task", new
        {
            ActivityId = activityId,
            ImportanceId = importanceId,
            StartTime = new { Hours = 8, Minutes = 0 },
            EndTime = new { Hours = 9, Minutes = 0 },
            IsBackground = false,
            IsActive = true,
            RecurrenceType = "DayType",
            ScheduledForDayTypes = new[] { "Vacation", "SickDay" },
            AutoPlace = true
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact(DisplayName = "FO6: editing the rule updates its untouched occurrences from today on, and only those")]
    public async Task Edit_ReachesUntouchedOccurrencesOnly()
    {
        var (activityId, importanceId) = await SeedReferencesAsync();
        var ruleId = await SeedRuleAsync(activityId: activityId, importanceId: importanceId);
        await GetDayPlanAsync(Today);

        var occurrences = await OccurrencesAsync(ruleId);
        var todays = occurrences.Single(o => o.Date == Today).Task;
        var tomorrows = occurrences.Single(o => o.Date == Tomorrow).Task;

        // Tomorrow's is moved inside its own day — not a slip, so no original slot, but the user's now.
        var move = await CreateClient().PatchAsJsonAsync($"api/planner-task/{tomorrows.Id}", new
        {
            StartTime = new { Hours = 20, Minutes = 0 },
            EndTime = new { Hours = 21, Minutes = 0 }
        }, JsonOpts, CancellationToken);
        move.StatusCode.Should().Be(HttpStatusCode.NoContent, await move.Content.ReadAsStringAsync(CancellationToken));

        // Yesterday's was placed on the old rule and is in the past.
        var yesterdaysId = await SeedOccurrenceAsync(ruleId, activityId, importanceId, Today.AddDays(-1));

        await PutRuleAsync(ruleId, activityId, importanceId, startHour: 7, autoPlace: true);

        (await TaskAsync(todays.Id)).StartTime.Should().Be(new TimeOnly(7, 0), "untouched, so it follows the rule");
        (await TaskAsync(tomorrows.Id)).StartTime.Should().Be(new TimeOnly(20, 0), "the user moved it");
        (await TaskAsync(yesterdaysId)).StartTime.Should().Be(new TimeOnly(18, 0), "a past day is a record, not a plan");
        (await OccurrencesAsync(ruleId)).Should().HaveCount(3, "an edit updates in place rather than adding");
    }

    [Fact(DisplayName = "FO6: switching auto-place off takes the untouched occurrences away, and leaves a skipped one")]
    public async Task Edit_SwitchingOff_RemovesUntouchedOnly()
    {
        var (activityId, importanceId) = await SeedReferencesAsync();
        var ruleId = await SeedRuleAsync(activityId: activityId, importanceId: importanceId);
        await GetDayPlanAsync(Today);
        var todays = (await OccurrencesAsync(ruleId)).Single(o => o.Date == Today).Task;

        var skip = await CreateClient().PatchAsJsonAsync($"api/planner-task/{todays.Id}/status",
            new { Status = "Cancelled" }, JsonOpts, CancellationToken);
        skip.StatusCode.Should().Be(HttpStatusCode.NoContent, await skip.Content.ReadAsStringAsync(CancellationToken));

        await PutRuleAsync(ruleId, activityId, importanceId, startHour: 18, autoPlace: false);

        var left = await OccurrencesAsync(ruleId);
        left.Should().ContainSingle().Which.Task.Id.Should().Be(todays.Id);
    }

    [Fact(DisplayName = "FO6: adding today to a placing rule puts today's block on, without reviving a deleted one")]
    public async Task Edit_NewlyMatchedDecidedDay_IsPlaced()
    {
        var (activityId, importanceId) = await SeedReferencesAsync();
        // Tomorrow only: today is decided (no match) by the first read.
        var ruleId = await SeedRuleAsync(activityId: activityId, importanceId: importanceId,
            days: [Tomorrow.DayOfWeek.ToString()]);
        await GetDayPlanAsync(Today);
        var tomorrows = (await OccurrencesAsync(ruleId)).Single().Task;
        var delete = await CreateClient().DeleteAsync($"api/planner-task/{tomorrows.Id}", CancellationToken);
        delete.IsSuccessStatusCode.Should().BeTrue();

        await PutRuleAsync(ruleId, activityId, importanceId, startHour: 18, autoPlace: true,
            days: [Today.DayOfWeek.ToString(), Tomorrow.DayOfWeek.ToString()]);

        (await OccurrencesAsync(ruleId)).Select(o => o.Date).Should().BeEquivalentTo([Today],
            "today is newly matched; tomorrow matched before and its occurrence was deleted");
    }

    [Fact(DisplayName = "FO6: an edit moving an untouched occurrence beside one block moves it in place")]
    public async Task Edit_MovingBesideOneBlock_MovesInPlace()
    {
        var (activityId, importanceId) = await SeedReferencesAsync();
        var ruleId = await SeedRuleAsync(activityId: activityId, importanceId: importanceId);
        await GetDayPlanAsync(Today);
        var todays = (await OccurrencesAsync(ruleId)).Single(o => o.Date == Today).Task;
        await SeedBlockAsync(Today, new TimeOnly(7, 0), new TimeOnly(8, 0));

        await PutRuleAsync(ruleId, activityId, importanceId, startHour: 7, autoPlace: true);

        var moved = await TaskAsync(todays.Id);
        (moved.StartTime, moved.EndTime).Should().Be((new TimeOnly(7, 0), new TimeOnly(8, 0)),
            "a second block fits beside the user's, so the occurrence follows the rule");
        (await OccurrencesAsync(ruleId)).Should().HaveCount(2);
    }

    /// <summary>
    /// An edit moving an untouched occurrence to where it would be a third block leaves what placing the edited
    /// rule fresh would leave: nothing under <c>Ignore</c>, the stretch where it is a second block under
    /// <c>MergeIgnore</c>. Tomorrow has no block, so its occurrence just moves.
    /// </summary>
    [Theory(DisplayName = "FO6: an edit moving an untouched occurrence into a third block falls back to the conflict mode")]
    [InlineData(ApplyTemplateConflictResolutionEnum.Ignore)]
    [InlineData(ApplyTemplateConflictResolutionEnum.MergeIgnore)]
    public async Task Edit_MovingIntoAThirdBlock_FallsBackToTheMode(ApplyTemplateConflictResolutionEnum mode)
    {
        await SetConflictModeAsync(mode);
        var (activityId, importanceId) = await SeedReferencesAsync();
        var ruleId = await SeedRuleAsync(activityId: activityId, importanceId: importanceId);
        await GetDayPlanAsync(Today);
        var occurrences = await OccurrencesAsync(ruleId);
        var todays = occurrences.Single(o => o.Date == Today).Task;
        var tomorrows = occurrences.Single(o => o.Date == Tomorrow).Task;
        var shortId = await SeedBlockAsync(Today, new TimeOnly(7, 0), new TimeOnly(7, 30));
        var longId = await SeedBlockAsync(Today, new TimeOnly(7, 0), new TimeOnly(8, 0));

        await PutRuleAsync(ruleId, activityId, importanceId, startHour: 7, autoPlace: true);

        var onToday = (await OccurrencesAsync(ruleId)).Where(o => o.Date == Today).Select(o => o.Task).ToList();
        onToday.Should().NotContain(t => t.Id == todays.Id, "the untouched occurrence is replaced, not stretched into a third lane");
        if (mode == ApplyTemplateConflictResolutionEnum.Ignore)
            onToday.Should().BeEmpty();
        else
            onToday.Should().ContainSingle().Which.Should().Match<PlannerTask>(t =>
                t.StartTime == new TimeOnly(7, 30) && t.EndTime == new TimeOnly(8, 0));

        (await TaskAsync(tomorrows.Id)).StartTime.Should().Be(new TimeOnly(7, 0), "tomorrow is free");
        await ShouldBeUntouchedAsync(shortId, new TimeOnly(7, 0), new TimeOnly(7, 30));
        await ShouldBeUntouchedAsync(longId, new TimeOnly(7, 0), new TimeOnly(8, 0));
    }

    /// <summary>
    /// The lanes judge a move, not a rename: a day already holding three blocks at the occurrence's own span (a bulk
    /// path, or from before the lanes) still lets an edit that keeps the span reach it.
    /// </summary>
    [Fact(DisplayName = "FO6: an edit that keeps the span reaches an occurrence already among three")]
    public async Task Edit_KeepingTheSpan_IsNotJudgedByTheLanes()
    {
        var (activityId, importanceId) = await SeedReferencesAsync();
        var ruleId = await SeedRuleAsync(activityId: activityId, importanceId: importanceId);
        await GetDayPlanAsync(Today);
        var todays = (await OccurrencesAsync(ruleId)).Single(o => o.Date == Today).Task;
        await SeedBlockAsync(Today, new TimeOnly(18, 0), new TimeOnly(19, 0));
        await SeedBlockAsync(Today, new TimeOnly(18, 0), new TimeOnly(19, 0));

        long otherActivityId;
        await using (var db = CreateDbContext())
            otherActivityId = await PlanningTestSeedHelper.SeedActivityAsync(db, $"fo6-{Guid.NewGuid():N}", UserId, CancellationToken);
        await PutRuleAsync(ruleId, otherActivityId, importanceId, startHour: 18, autoPlace: true);

        var kept = await TaskAsync(todays.Id);
        kept.ActivityId.Should().Be(otherActivityId, "the span did not change, so the edit is not a move");
        kept.StartTime.Should().Be(new TimeOnly(18, 0));
    }

    /// <summary>
    /// The lanes judge only the minutes an edit newly covers, the same as the single-task writes: an occurrence
    /// already among three at 18:00–19:00 follows an edit that extends it into a free 19:00–20:00. Judged on its
    /// whole new span it would be a third and, under the default <c>Ignore</c>, be removed.
    /// </summary>
    [Fact(DisplayName = "FO6: an edit extending an occurrence among three into free time is judged on the added stretch")]
    public async Task Edit_ExtendingIntoFreeTime_IsJudgedOnTheAddedStretchOnly()
    {
        var (activityId, importanceId) = await SeedReferencesAsync();
        var ruleId = await SeedRuleAsync(activityId: activityId, importanceId: importanceId);
        await GetDayPlanAsync(Today);
        var todays = (await OccurrencesAsync(ruleId)).Single(o => o.Date == Today).Task;
        var firstId = await SeedBlockAsync(Today, new TimeOnly(18, 0), new TimeOnly(19, 0));
        var secondId = await SeedBlockAsync(Today, new TimeOnly(18, 0), new TimeOnly(19, 0));

        await PutRuleAsync(ruleId, activityId, importanceId, startHour: 18, autoPlace: true, endHour: 20);

        var extended = await TaskAsync(todays.Id);
        (extended.StartTime, extended.EndTime).Should().Be((new TimeOnly(18, 0), new TimeOnly(20, 0)),
            "only 19:00-20:00 is new, and it is free");
        await ShouldBeUntouchedAsync(firstId, new TimeOnly(18, 0), new TimeOnly(19, 0));
        await ShouldBeUntouchedAsync(secondId, new TimeOnly(18, 0), new TimeOnly(19, 0));
    }

    [Fact(DisplayName = "FO6: copying a day does not carry the occurrence's rule onto the copy")]
    public async Task CopyToDays_DropsTheSource()
    {
        var ruleId = await SeedRuleAsync();
        await GetDayPlanAsync(Today);
        var todaysCalendarId = (await OccurrencesAsync(ruleId)).Single(o => o.Date == Today).Task.CalendarId;

        long targetId;
        await using (var db = CreateDbContext())
            targetId = await PlanningTestSeedHelper.SeedCalendarAsync(db, Today.AddDays(5), UserId, CancellationToken);

        var copy = await CreateClient().PostAsJsonAsync("api/planner-task/copy-to-days",
            new { SourceCalendarId = todaysCalendarId, TargetCalendarIds = new[] { targetId } }, JsonOpts, CancellationToken);
        copy.StatusCode.Should().Be(HttpStatusCode.OK, await copy.Content.ReadAsStringAsync(CancellationToken));

        await using var check = CreateDbContext();
        var copied = await check.Set<PlannerTask>().IgnoreQueryFilters()
            .Where(t => t.CalendarId == targetId).ToListAsync(CancellationToken);
        copied.Should().ContainSingle().Which.SourceRepeatingTaskId.Should().BeNull();
    }

    // ---------------------------------------------------------------- helpers

    private async Task<long> SeedRuleAsync(
        long userId = UserId, bool autoPlace = true, bool isActive = true, string[]? days = null,
        long? activityId = null, long? importanceId = null, bool isBackground = false)
    {
        await using var db = CreateDbContext();
        var rule = new RepeatingPlannerTask
        {
            UserId = userId,
            ActivityId = activityId
                         ?? await PlanningTestSeedHelper.SeedActivityAsync(db, $"fo6-{Guid.NewGuid():N}", userId, CancellationToken),
            ImportanceId = importanceId,
            StartTime = new TimeOnly(18, 0),
            EndTime = new TimeOnly(19, 0),
            IsBackground = isBackground,
            IsActive = isActive,
            AutoPlace = autoPlace,
            RecurrenceType = RecurrenceType.DayOfWeek,
            ScheduledDays = (days ?? EveryDay).ToList()
        };
        db.Set<RepeatingPlannerTask>().Add(rule);
        await db.SaveChangesAsync(CancellationToken);
        return rule.Id;
    }

    private async Task<(long ActivityId, long ImportanceId)> SeedReferencesAsync()
    {
        await using var db = CreateDbContext();
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, $"fo6-{Guid.NewGuid():N}", UserId, CancellationToken);
        var importanceId = await PlanningTestSeedHelper.SeedTaskImportanceAsync(db, Random.Shared.Next(1000, 100000), UserId, ct: CancellationToken);
        return (activityId, importanceId);
    }

    /// <summary>An occurrence the way the materialiser writes one, on a day it would not reach itself.</summary>
    private async Task<long> SeedOccurrenceAsync(long ruleId, long activityId, long importanceId, DateOnly date)
    {
        await using var db = CreateDbContext();
        var calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, date, UserId, CancellationToken);
        var task = new PlannerTask
        {
            UserId = UserId,
            CalendarId = calendarId,
            ActivityId = activityId,
            ImportanceId = importanceId,
            StartTime = new TimeOnly(18, 0),
            EndTime = new TimeOnly(19, 0),
            IsBackground = false,
            Status = PlannerTaskStatus.NotStarted,
            SourceRepeatingTaskId = ruleId
        };
        db.Set<PlannerTask>().Add(task);
        await db.SaveChangesAsync(CancellationToken);
        return task.Id;
    }

    /// <summary>A block the user placed, written straight to the table so no lane check stands in the way.</summary>
    private async Task<long> SeedBlockAsync(DateOnly date, TimeOnly start, TimeOnly end, bool isBackground = false)
    {
        await using var db = CreateDbContext();
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, $"blocker-{Guid.NewGuid():N}", UserId, CancellationToken);
        var calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, date, UserId, CancellationToken);
        var task = new PlannerTask
        {
            UserId = UserId,
            CalendarId = calendarId,
            ActivityId = activityId,
            StartTime = start,
            EndTime = end,
            IsBackground = isBackground,
            Status = PlannerTaskStatus.NotStarted
        };
        db.Set<PlannerTask>().Add(task);
        await db.SaveChangesAsync(CancellationToken);
        return task.Id;
    }

    /// <summary>Null leaves the user without a settings row, which reads as <c>Ignore</c>.</summary>
    private async Task SetConflictModeAsync(ApplyTemplateConflictResolutionEnum? mode)
    {
        if (mode is not { } chosen)
            return;
        await using var db = CreateDbContext();
        db.Set<UserPlannerSettings>().Add(new UserPlannerSettings { UserId = UserId, DefaultConflictResolution = chosen });
        await db.SaveChangesAsync(CancellationToken);
    }

    private async Task ShouldBeUntouchedAsync(long taskId, TimeOnly start, TimeOnly end)
    {
        await using var db = CreateDbContext();
        var task = await db.Set<PlannerTask>().IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(t => t.Id == taskId, CancellationToken);
        task.Should().NotBeNull("the materialiser never deletes the user's block");
        (task!.StartTime, task.EndTime).Should().Be((start, end), "the materialiser never moves or carves the user's block");
    }

    private async Task SetDayTypeAsync(DateOnly date, DayType dayType)
    {
        await using var db = CreateDbContext();
        var calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, date, UserId, CancellationToken);
        var calendar = await db.Set<Calendar>().IgnoreQueryFilters().SingleAsync(c => c.Id == calendarId, CancellationToken);
        calendar.DayType = dayType;
        await db.SaveChangesAsync(CancellationToken);
    }

    /// <summary>Through the bulk bar's endpoint, so the day-type event is really published.</summary>
    private async Task MarkDayTypeAsync(DateOnly date, string dayType)
    {
        long calendarId;
        await using (var db = CreateDbContext())
            calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, date, UserId, CancellationToken);

        var response = await CreateClient().PatchAsJsonAsync("api/calendar/day-type/batch",
            new { CalendarIds = new[] { calendarId }, DayType = dayType }, JsonOpts, CancellationToken);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        body.Should().NotContain("\"succeeded\":false", body);
    }

    private static object RuleBody(
        long activityId, long importanceId, int startHour, bool autoPlace, string[]? days = null, int? endHour = null) => new
    {
        ActivityId = activityId,
        ImportanceId = importanceId,
        StartTime = new { Hours = startHour, Minutes = 0 },
        EndTime = new { Hours = endHour ?? startHour + 1, Minutes = 0 },
        IsBackground = false,
        IsActive = true,
        RecurrenceType = "DayOfWeek",
        ScheduledDays = days ?? EveryDay,
        AutoPlace = autoPlace
    };

    private async Task PutRuleAsync(
        long ruleId, long activityId, long importanceId, int startHour, bool autoPlace, string[]? days = null, int? endHour = null)
    {
        var response = await CreateClient().PutAsJsonAsync($"api/repeating-planner-task/{ruleId}",
            RuleBody(activityId, importanceId, startHour, autoPlace, days, endHour), JsonOpts, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(CancellationToken));
    }

    private async Task<DayPlanResponse> GetDayPlanAsync(DateOnly date)
    {
        var response = await CreateClient().GetAsync($"api/calendar/day-plan/{date:yyyy-MM-dd}", CancellationToken);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonSerializer.Deserialize<DayPlanResponse>(body, JsonOpts)!;
    }

    private async Task<List<(DateOnly Date, PlannerTask Task)>> OccurrencesAsync(long ruleId)
    {
        await using var db = CreateDbContext();
        var rows = await db.Set<PlannerTask>().IgnoreQueryFilters().AsNoTracking()
            .Include(t => t.Calendar)
            .Where(t => t.SourceRepeatingTaskId == ruleId)
            .ToListAsync(CancellationToken);
        return rows.Select(t => (t.Calendar.Date, t)).OrderBy(o => o.Date).ToList();
    }

    private async Task<PlannerTask> TaskAsync(long taskId)
    {
        await using var db = CreateDbContext();
        return await db.Set<PlannerTask>().IgnoreQueryFilters().AsNoTracking().SingleAsync(t => t.Id == taskId, CancellationToken);
    }

    private async Task<RepeatingPlannerTask> RuleAsync(long ruleId)
    {
        await using var db = CreateDbContext();
        return await db.Set<RepeatingPlannerTask>().IgnoreQueryFilters().AsNoTracking().SingleAsync(r => r.Id == ruleId, CancellationToken);
    }
}
