using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.Core.domain.model.@enum;
using AdhdTimeOrganizer.IntegrationTests.Endpoints;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using AdhdTimeOrganizer.Routines.application.dto.response.todoList;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Routines;

/// <summary>
/// FO1 — vacation and sick days stop breaking routines, asserted on rows. The pure rule is pinned in
/// <c>Services/RoutineAwayExcusalTests</c>; these pin the two mechanisms that fail silently around it:
/// <list type="bullet">
///   <item>a reset site must actually read the away days through Planning's <c>ICalendarDayLookup</c> — one that
///   did not would score every away cycle as a miss and nothing would error;</item>
///   <item>the retroactive path is a cross-slice event whose handler logs and swallows, so the only evidence it
///   ran is the completion row and the period's streak.</item>
/// </list>
/// </summary>
[Collection("Postgres")]
public class RoutineAwayExcusalFlowTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    // ─── At reset: the grouped read ──────────────────────────────────────────

    /// <summary>
    /// The first morning back: opening the routines screen closes the two daily cycles spent on holiday. Both
    /// are excused, the period and the item keep their streaks, and the consistency figure does not count them.
    /// A three-day period of which only one day was away is the control: it breaks as it always did.
    /// </summary>
    [Fact(DisplayName = "FO1: the grouped read excuses the cycles the user was away for, and only those")]
    public async Task GroupedRead_ExcusesCyclesSpentAway()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        long dailyId, dailyItemId, controlId, controlItemId;

        await using (var db = CreateDbContext())
        {
            var daily = await SeedPeriodAsync(db, lengthInDays: 1, lastResetAt: Midnight(today.AddDays(-2)), streak: 5);
            dailyId = daily.Id;
            dailyItemId = (await SeedItemAsync(db, daily, streak: 7)).Id;

            var control = await SeedPeriodAsync(db, lengthInDays: 3, lastResetAt: Midnight(today.AddDays(-4)), streak: 5);
            controlId = control.Id;
            controlItemId = (await SeedItemAsync(db, control, streak: 7)).Id;

            await PlanningTestSeedHelper.SeedCalendarAsync(db, today.AddDays(-2), UserId, CancellationToken, DayType.Vacation);
            await PlanningTestSeedHelper.SeedCalendarAsync(db, today.AddDays(-1), UserId, CancellationToken, DayType.SickDay);
        }

        var response = await CreateClient().GetAsync("api/routine-todo-list/grouped-by-time-period", CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var groups = (await response.Content.ReadFromJsonAsync<List<RoutineTodoListGroupedResponse>>(JsonOpts, CancellationToken))!;

        var served = groups.Single(g => g.RoutineTimePeriod.Id == dailyId).RoutineTimePeriod;
        served.CompletionHistory.Should().HaveCount(2).And.OnlyContain(c => c.IsExcused && !c.IsFrozen);
        served.TotalPeriodsElapsed.Should().Be(0, "an excused cycle does not count against the consistency figure");

        await using var verify = CreateDbContext();
        (await verify.Set<RoutinePeriodCompletion>().Where(c => c.TimePeriodId == dailyId).ToListAsync(CancellationToken))
            .Should().HaveCount(2).And.OnlyContain(c => c.IsExcused);
        (await verify.Set<RoutineTimePeriod>().SingleAsync(p => p.Id == dailyId, CancellationToken)).Streak.Should().Be(5);
        (await verify.Set<RoutineTodoList>().SingleAsync(i => i.Id == dailyItemId, CancellationToken)).Streak.Should().Be(7);

        // One of the control's three days was away: not half, so the cycle is a plain miss.
        (await verify.Set<RoutinePeriodCompletion>().SingleAsync(c => c.TimePeriodId == controlId, CancellationToken))
            .IsExcused.Should().BeFalse();
        (await verify.Set<RoutineTimePeriod>().SingleAsync(p => p.Id == controlId, CancellationToken)).Streak.Should().Be(0);
        (await verify.Set<RoutineTodoList>().SingleAsync(i => i.Id == controlItemId, CancellationToken)).Streak.Should().Be(0);
    }

    // ─── Retroactively: the day-type event ───────────────────────────────────

    private static readonly DateOnly Week0 = new(2026, 3, 2); // Monday

    /// <summary>
    /// Back from a flu, the user marks Monday to Thursday of last week as sick. That week's cycle was already
    /// closed and broke a run of two. Marking the days excuses it, clears the grace window it opened, and the
    /// run comes back. A frozen cycle over the same days is the control: it was already covered, and the CHECK
    /// forbids it being both.
    /// </summary>
    [Fact(DisplayName = "FO1: marking past days as away through the batch excuses the closed cycle and restores the streak")]
    public async Task BatchDayType_ExcusesAClosedCycle_AndRestoresTheStreak()
    {
        var week2 = Week0.AddDays(14);
        long weeklyId, frozenPeriodId;
        var dayIds = new List<long>();

        await using (var db = CreateDbContext())
        {
            // No reset due: the period was reset yesterday, so only the retroactive path can touch it.
            var weekly = await SeedPeriodAsync(db, lengthInDays: 7, lastResetAt: DateTime.UtcNow.AddDays(-1), streak: 0);
            weekly.BestStreak = 2;
            weekly.StreakGraceUntil = DateTime.UtcNow.AddDays(2);
            weekly.GraceNotifiedFor = weekly.StreakGraceUntil;
            weeklyId = weekly.Id;

            var withFreeze = await SeedPeriodAsync(db, lengthInDays: 6, lastResetAt: DateTime.UtcNow.AddDays(-1), streak: 0);
            frozenPeriodId = withFreeze.Id;
            await db.SaveChangesAsync(CancellationToken);

            db.Set<RoutinePeriodCompletion>().AddRange(
                Completion(weeklyId, Week0, 7, completed: 1),
                Completion(weeklyId, Week0.AddDays(7), 7, completed: 1),
                Completion(weeklyId, week2, 7, completed: 0),
                Completion(frozenPeriodId, week2, 6, completed: 0, frozen: true));
            await db.SaveChangesAsync(CancellationToken);

            for (var offset = 0; offset < 4; offset++)
                dayIds.Add(await PlanningTestSeedHelper.SeedCalendarAsync(db, week2.AddDays(offset), UserId, CancellationToken));
        }

        var response = await CreateClient().PatchAsJsonAsync("api/calendar/day-type/batch", new
        {
            CalendarIds = dayIds,
            DayType = "SickDay"
        }, JsonOpts, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var verify = CreateDbContext();
        var history = await verify.Set<RoutinePeriodCompletion>()
            .Where(c => c.TimePeriodId == weeklyId).OrderBy(c => c.PeriodStart).ToListAsync(CancellationToken);
        history.Select(c => c.IsExcused).Should().Equal(false, false, true);

        var weeklyAfter = await verify.Set<RoutineTimePeriod>().SingleAsync(p => p.Id == weeklyId, CancellationToken);
        weeklyAfter.Streak.Should().Be(2, "the excused week carries the run of the two met weeks before it");
        weeklyAfter.StreakGraceUntil.Should().BeNull("the excused cycle opened that window, and it is no longer a miss");
        weeklyAfter.GraceNotifiedFor.Should().BeNull();

        var frozen = await verify.Set<RoutinePeriodCompletion>().SingleAsync(c => c.TimePeriodId == frozenPeriodId, CancellationToken);
        frozen.IsFrozen.Should().BeTrue();
        frozen.IsExcused.Should().BeFalse("a frozen cycle is already covered — it is never also excused");
    }

    /// <summary>
    /// The single-day edit publishes the same event, and only when the type actually changed — read off the
    /// tracked original value, which is the part a refactor could quietly break.
    /// </summary>
    [Fact(DisplayName = "FO1: marking one past day sick through the Details dialog excuses that day's cycle")]
    public async Task UpdateCalendar_MarkingADaySick_ExcusesItsCycle()
    {
        var sickDay = Week0.AddDays(2);
        long dailyId, calendarId;

        await using (var db = CreateDbContext())
        {
            var daily = await SeedPeriodAsync(db, lengthInDays: 1, lastResetAt: DateTime.UtcNow.AddHours(-1), streak: 0);
            daily.BestStreak = 2;
            dailyId = daily.Id;
            await db.SaveChangesAsync(CancellationToken);

            db.Set<RoutinePeriodCompletion>().AddRange(
                Completion(dailyId, sickDay.AddDays(-2), 1, completed: 1),
                Completion(dailyId, sickDay.AddDays(-1), 1, completed: 1),
                Completion(dailyId, sickDay, 1, completed: 0));
            await db.SaveChangesAsync(CancellationToken);

            calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, sickDay, UserId, CancellationToken);
        }

        var response = await CreateClient().PutAsJsonAsync($"api/calendar/{calendarId}", new
        {
            DayType = "SickDay",
            WakeUpTime = new { Hours = 7, Minutes = 0 },
            BedTime = new { Hours = 23, Minutes = 0 }
        }, JsonOpts, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var verify = CreateDbContext();
        (await verify.Set<RoutinePeriodCompletion>().SingleAsync(c => c.TimePeriodId == dailyId && c.PeriodStart == sickDay, CancellationToken))
            .IsExcused.Should().BeTrue();
        (await verify.Set<RoutineTimePeriod>().SingleAsync(p => p.Id == dailyId, CancellationToken)).Streak.Should().Be(2);
    }

    /// <summary>
    /// F4 question 4 (owner decision, 2026-10-15): a public holiday ranks like a weekend in the planner, and is
    /// <b>not</b> time away. The week of Good Friday 2026, its days created the way the app creates them, so Friday is a
    /// <c>Workday</c> row carrying the holiday key. Three days marked sick are three of seven, one short of the
    /// majority, and the holiday is the day that would tip it: if a holiday ever counted as away, this cycle would be
    /// excused. Marking a fourth day is the control that the retroactive path ran at all. Without it, "not excused"
    /// would also pass for a handler that never fired.
    /// </summary>
    [Fact(DisplayName = "F4 Q4: a weekly cycle containing a holiday is not excused for it")]
    public async Task WeeklyCycleWithAHoliday_IsNotExcusedForTheHoliday()
    {
        var goodFriday = PlanningTestSeedHelper.HolidayDate(2026, "GOOD_FRIDAY");
        var weekStart = goodFriday.AddDays(-4); // Monday
        long weeklyId;
        var dayIds = new List<long>();

        await using (var db = CreateDbContext())
        {
            var weekly = await SeedPeriodAsync(db, lengthInDays: 7, lastResetAt: DateTime.UtcNow.AddDays(-1), streak: 0);
            weekly.BestStreak = 2;
            weeklyId = weekly.Id;
            await db.SaveChangesAsync(CancellationToken);

            db.Set<RoutinePeriodCompletion>().AddRange(
                Completion(weeklyId, weekStart.AddDays(-14), 7, completed: 1),
                Completion(weeklyId, weekStart.AddDays(-7), 7, completed: 1),
                Completion(weeklyId, weekStart, 7, completed: 0));
            await db.SaveChangesAsync(CancellationToken);

            for (var offset = 0; offset < 7; offset++)
                dayIds.Add(await PlanningTestSeedHelper.SeedNaturalCalendarAsync(db, weekStart.AddDays(offset), UserId, CancellationToken));
        }

        await using (var check = CreateDbContext())
        {
            var holiday = await check.Set<Calendar>().IgnoreQueryFilters().SingleAsync(c => c.Id == dayIds[4], CancellationToken);
            holiday.Date.Should().Be(goodFriday);
            holiday.DayType.Should().Be(DayType.Workday);
            holiday.HolidayKey.Should().Be("GOOD_FRIDAY");
        }

        // Monday to Wednesday sick: three away days. Good Friday would be the fourth.
        (await MarkSickAsync(dayIds.Take(3))).StatusCode.Should().Be(HttpStatusCode.OK);

        await using (var afterThree = CreateDbContext())
        {
            (await afterThree.Set<RoutinePeriodCompletion>().SingleAsync(c => c.TimePeriodId == weeklyId && c.PeriodStart == weekStart, CancellationToken))
                .IsExcused.Should().BeFalse("a holiday is not time away, so three sick days are not the majority of the week");
            (await afterThree.Set<RoutineTimePeriod>().SingleAsync(p => p.Id == weeklyId, CancellationToken)).Streak.Should().Be(0);
            (await afterThree.Set<Calendar>().IgnoreQueryFilters().SingleAsync(c => c.Id == dayIds[4], CancellationToken))
                .DayType.Should().Be(DayType.Workday, "nothing on the excusal path rewrites the holiday");
        }

        // Thursday too: four of seven, without the holiday. The path is live.
        (await MarkSickAsync([dayIds[3]])).StatusCode.Should().Be(HttpStatusCode.OK);

        await using var verify = CreateDbContext();
        (await verify.Set<RoutinePeriodCompletion>().SingleAsync(c => c.TimePeriodId == weeklyId && c.PeriodStart == weekStart, CancellationToken))
            .IsExcused.Should().BeTrue();
        (await verify.Set<RoutineTimePeriod>().SingleAsync(p => p.Id == weeklyId, CancellationToken)).Streak.Should().Be(2);
    }

    private async Task<HttpResponseMessage> MarkSickAsync(IEnumerable<long> calendarIds) =>
        await CreateClient().PatchAsJsonAsync("api/calendar/day-type/batch", new
        {
            CalendarIds = calendarIds.ToList(),
            DayType = "SickDay"
        }, JsonOpts, CancellationToken);

    // ─── The ledger's own guarantee ──────────────────────────────────────────

    [Fact(DisplayName = "FO1: the database refuses a cycle that is both frozen and excused")]
    public async Task Check_RefusesFrozenAndExcused()
    {
        await using var db = CreateDbContext();
        var period = await SeedPeriodAsync(db, lengthInDays: 7, lastResetAt: DateTime.UtcNow.AddDays(-1), streak: 0);

        var both = Completion(period.Id, Week0, 7, completed: 0, frozen: true);
        both.IsExcused = true;
        db.Set<RoutinePeriodCompletion>().Add(both);

        var save = () => db.SaveChangesAsync(CancellationToken);
        (await save.Should().ThrowAsync<DbUpdateException>())
            .WithInnerException<Npgsql.PostgresException>()
            .Which.ConstraintName.Should().Be("ck_routine_period_completion_not_frozen_and_excused");
    }

    // ─── helpers ─────────────────────────────────────────────────────────────

    private static DateTime Midnight(DateOnly date) => date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    private async Task<RoutineTimePeriod> SeedPeriodAsync(DbContext db, int lengthInDays, DateTime lastResetAt, int streak)
    {
        var period = new RoutineTimePeriod
        {
            UserId = UserId,
            // (user_id, text) and (user_id, length_in_days) are both unique.
            Text = $"Away period {lengthInDays}",
            Color = "#123456",
            LengthInDays = lengthInDays,
            ResetAnchorDay = 0,
            StreakThreshold = 100,
            StreakGraceDays = 0,
            LastResetAt = lastResetAt,
            Streak = streak,
            BestStreak = streak
        };
        db.Set<RoutineTimePeriod>().Add(period);
        await db.SaveChangesAsync(CancellationToken);
        return period;
    }

    private async Task<RoutineTodoList> SeedItemAsync(DbContext db, RoutineTimePeriod period, int streak)
    {
        var role = new ActivityRole { UserId = UserId, Name = $"Role {Guid.NewGuid():N}", Color = "#123456" };
        db.Set<ActivityRole>().Add(role);
        await db.SaveChangesAsync(CancellationToken);

        var activity = new Activity { UserId = UserId, Name = $"Activity {Guid.NewGuid():N}", RoleId = role.Id };
        db.Set<Activity>().Add(activity);
        await db.SaveChangesAsync(CancellationToken);

        var item = new RoutineTodoList
        {
            UserId = UserId,
            ActivityId = activity.Id,
            TimePeriodId = period.Id,
            Streak = streak,
            BestStreak = streak
        };
        db.Set<RoutineTodoList>().Add(item);
        await db.SaveChangesAsync(CancellationToken);
        return item;
    }

    private static RoutinePeriodCompletion Completion(long periodId, DateOnly start, int length, int completed, bool frozen = false) =>
        new()
        {
            TimePeriodId = periodId,
            PeriodStart = start,
            PeriodEnd = start.AddDays(length),
            CompletedCount = completed,
            TotalCount = 1,
            IsFrozen = frozen,
            FrozenAt = frozen ? new DateTime(2026, 3, 25, 0, 0, 0, DateTimeKind.Utc) : null
        };
}
