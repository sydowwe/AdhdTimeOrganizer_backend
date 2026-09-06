using System.Security.Claims;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.IntegrationTests.Reminders;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.Routines.domain.model.@enum;
using AdhdTimeOrganizer.Routines.domain.serviceContract;
using AdhdTimeOrganizer.Routines.infrastructure.jobs;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Sydowwe.Framework.Contracts.scheduling;
using Sydowwe.Framework.domain.extServiceContract.user;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Routines;

/// <summary>
/// <see cref="RoutineTodoListResetJobHandler"/>'s query shape and save gating — the parts a pure
/// <c>RoutineResetService</c> unit test cannot reach, since the handler decides <i>what</i> gets loaded and
/// <i>when</i> the sweep is worth persisting.
/// <para>
/// <see cref="RoutineNotificationTests"/> already covers that the end-of-period summary notification fires
/// and does not duplicate; these tests do not repeat that, they cover the reset mechanics underneath it.
/// </para>
/// </summary>
[Collection("Postgres")]
public class RoutineTodoListResetJobHandlerTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    // ---- seeding helpers ---------------------------------------------------------------------------

    private static async Task<RoutineTimePeriod> SeedPeriodAsync(
        DbContext db,
        long userId = UserId,
        int lengthInDays = 7,
        int resetAnchorDay = 0,
        DateTime? lastResetAt = null,
        int streakThreshold = 100,
        int streakGraceDays = 0,
        DateTime? streakGraceUntil = null,
        int streak = 0,
        int bestStreak = 0,
        CancellationToken ct = default)
    {
        var period = new RoutineTimePeriod
        {
            UserId = userId,
            // Unique per test: routine_time_period has unique indexes on (user_id, text) and (user_id, length_in_days).
            Text = $"Period {Guid.NewGuid():N}",
            Color = "#123456",
            LengthInDays = lengthInDays,
            ResetAnchorDay = resetAnchorDay,
            StreakThreshold = streakThreshold,
            StreakGraceDays = streakGraceDays,
            StreakGraceUntil = streakGraceUntil,
            Streak = streak,
            BestStreak = bestStreak,
            LastResetAt = lastResetAt
        };
        db.Set<RoutineTimePeriod>().Add(period);
        await db.SaveChangesAsync(ct);
        return period;
    }

    private static async Task<RoutineTodoList> SeedItemAsync(
        DbContext db, RoutineTimePeriod period, long userId, bool isDone, CancellationToken ct, int stepCount = 0)
    {
        var role = new ActivityRole { UserId = userId, Name = $"Role {Guid.NewGuid():N}", Color = "#123456" };
        db.Set<ActivityRole>().Add(role);
        await db.SaveChangesAsync(ct);

        var activity = new Activity { UserId = userId, Name = $"Activity {Guid.NewGuid():N}", RoleId = role.Id };
        db.Set<Activity>().Add(activity);
        await db.SaveChangesAsync(ct);

        var item = new RoutineTodoList
        {
            UserId = userId,
            ActivityId = activity.Id,
            TimePeriodId = period.Id,
            IsDone = isDone
        };

        if (stepCount > 0)
        {
            item.TotalCount = stepCount;
            item.DoneCount = isDone ? stepCount : 0;
            for (var i = 0; i < stepCount; i++)
                item.Steps.Add(new TodoListStep { Name = $"Step {i}", Order = i, IsDone = isDone });
        }

        db.Set<RoutineTodoList>().Add(item);
        await db.SaveChangesAsync(ct);
        return item;
    }

    /// <summary>
    /// Runs the handler the way the Scheduler's dispatcher would: one DI scope per fire, a hand-built
    /// <see cref="ScheduledJobContext"/> (the handler reads nothing off it — its state all comes from the
    /// database). Defaults to the ordinary host factory; the notifier-failure test swaps in its own.
    /// </summary>
    private async Task RunJobAsync(IServiceProvider? services = null, int? userPageSize = null)
    {
        await using var scope = (services ?? Fixture.AdminAndUserFactory.Services).CreateAsyncScope();
        // PERF-3: the sweep pages by user. Passing 1 puts every user on their own page, which is the only way
        // to cross a page boundary without seeding RoutineSweepPaging.UserPageSize users; omitting it takes the
        // constructor default, which is what DI and the dispatcher do.
        var handler = userPageSize is { } size
            ? ActivatorUtilities.CreateInstance<RoutineTodoListResetJobHandler>(scope.ServiceProvider, size)
            : ActivatorUtilities.CreateInstance<RoutineTodoListResetJobHandler>(scope.ServiceProvider);

        var now = DateTime.UtcNow;
        await handler.ExecuteAsync(new ScheduledJobContext
        {
            ScheduledFireTimeUtc = now,
            ActualFireTimeUtc = now,
            JobKey = handler.Key,
            CorrelationId = "routine-reset-job-test",
            TriggerSource = TriggerSource.Manual
        }, CancellationToken);
    }

    // ---- CQ-3: items and steps both unticked ------------------------------------------------------

    [Fact]
    public async Task ResetSweep_UnticksTheItemAndAllOfItsSteps()
    {
        RoutineTimePeriod period;
        RoutineTodoList item;
        await using (var db = CreateDbContext())
        {
            period = await SeedPeriodAsync(db, lastResetAt: DateTime.UtcNow.AddDays(-8), ct: CancellationToken);
            item = await SeedItemAsync(db, period, UserId, isDone: true, CancellationToken, stepCount: 3);
        }

        await RunJobAsync();

        // Fresh context: a tracked in-memory graph from the seeding context would mask a query shape that
        // forgets to load Steps.
        await using var verify = CreateDbContext();
        var stored = await verify.Set<RoutineTodoList>().AsNoTracking()
            .Include(i => i.Steps)
            .FirstAsync(i => i.Id == item.Id, CancellationToken);

        stored.IsDone.Should().BeFalse();
        stored.Steps.Should().HaveCount(3).And.OnlyContain(s => !s.IsDone);
    }

    // ---- CQ-2: grace expiry persists even with nothing due to reset --------------------------------

    [Fact]
    public async Task ResetSweep_PersistsGraceExpiry_WhenNoPeriodIsDueForReset()
    {
        RoutineTimePeriod period;
        await using (var db = CreateDbContext())
        {
            // Just reset: ComputeNextReset is a full period out, so nothing about this period is due —
            // the only thing the sweep should touch is the lapsed grace.
            period = await SeedPeriodAsync(
                db,
                lastResetAt: DateTime.UtcNow,
                streak: 5,
                bestStreak: 5,
                streakGraceUntil: DateTime.UtcNow.AddDays(-1),
                ct: CancellationToken);
        }

        await RunJobAsync();

        await using var verify = CreateDbContext();
        var stored = await verify.Set<RoutineTimePeriod>().AsNoTracking()
            .FirstAsync(p => p.Id == period.Id, CancellationToken);

        stored.Streak.Should().Be(0);
        stored.StreakGraceUntil.Should().BeNull();
    }

    /// <summary>The mirror case: a lapsed grace on one period must not be dropped by a save gated on another
    /// period actually resetting.</summary>
    [Fact]
    public async Task ResetSweep_PersistsGraceExpiry_AlongsideAPeriodThatIsDueForReset()
    {
        RoutineTimePeriod gracePeriod;
        await using (var db = CreateDbContext())
        {
            // Different LengthInDays from duePeriod below: routine_time_period has a unique index on
            // (user_id, length_in_days).
            gracePeriod = await SeedPeriodAsync(
                db,
                lengthInDays: 5,
                lastResetAt: DateTime.UtcNow,
                streak: 5,
                bestStreak: 5,
                streakGraceUntil: DateTime.UtcNow.AddDays(-1),
                ct: CancellationToken);

            var duePeriod = await SeedPeriodAsync(db, lastResetAt: DateTime.UtcNow.AddDays(-8), ct: CancellationToken);
            await SeedItemAsync(db, duePeriod, UserId, isDone: true, CancellationToken);
        }

        await RunJobAsync();

        await using var verify = CreateDbContext();
        var stored = await verify.Set<RoutineTimePeriod>().AsNoTracking()
            .FirstAsync(p => p.Id == gracePeriod.Id, CancellationToken);

        stored.Streak.Should().Be(0);
        stored.StreakGraceUntil.Should().BeNull();
    }

    // ---- idempotency / double-fire ------------------------------------------------------------------

    [Fact]
    public async Task ResetSweep_DoubleFire_ProducesExactlyOneCompletionRowAndOneStreakAdvance()
    {
        RoutineTimePeriod period;
        var originalLastResetAt = DateTime.UtcNow.AddDays(-8);
        await using (var db = CreateDbContext())
        {
            period = await SeedPeriodAsync(db, lastResetAt: originalLastResetAt, ct: CancellationToken);
            await SeedItemAsync(db, period, UserId, isDone: true, CancellationToken);
        }

        await RunJobAsync();
        await RunJobAsync();

        await using var verify = CreateDbContext();
        var completions = await verify.Set<RoutinePeriodCompletion>().AsNoTracking()
            .Where(c => c.TimePeriodId == period.Id)
            .ToListAsync(CancellationToken);
        completions.Should().ContainSingle();

        var stored = await verify.Set<RoutineTimePeriod>().AsNoTracking()
            .FirstAsync(p => p.Id == period.Id, CancellationToken);
        stored.Streak.Should().Be(1);
        stored.LastResetAt.Should().Be(originalLastResetAt.AddDays(7).Date);
    }

    // ---- CQ-1: calendar-anchored periods ---------------------------------------------------------------

    /// <summary>
    /// Every user is seeded a Monthly (30d, anchor 1) and a Yearly (365d, anchor 1), and those two branches used
    /// to compute a boundary in the future unconditionally — so the sweep, which runs at 02:00, could never
    /// reach one. The whole default configuration silently never reset. The other tests in this file all use
    /// <c>ResetAnchorDay = 0</c>, which is exactly the branch that always worked.
    /// </summary>
    [Fact]
    public async Task ResetSweep_MonthlyAnchoredPeriod_ResetsWhenItsAnchorHasPassed()
    {
        RoutineTimePeriod period;
        RoutineTodoList item;
        var now = DateTime.UtcNow;
        await using (var db = CreateDbContext())
        {
            // Relative to UtcNow rather than a fixed date, so the period is due on every calendar day.
            period = await SeedPeriodAsync(
                db, lengthInDays: 30, resetAnchorDay: 1, lastResetAt: now.AddMonths(-2), ct: CancellationToken);
            item = await SeedItemAsync(db, period, UserId, isDone: true, CancellationToken);
        }

        await RunJobAsync();

        await using var verify = CreateDbContext();
        var stored = await verify.Set<RoutineTimePeriod>().AsNoTracking()
            .FirstAsync(p => p.Id == period.Id, CancellationToken);
        // The catch-up walks to the newest anchor that has passed: the 1st of the current month.
        stored.LastResetAt.Should().Be(new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc));

        var storedItem = await verify.Set<RoutineTodoList>().AsNoTracking()
            .FirstAsync(i => i.Id == item.Id, CancellationToken);
        storedItem.IsDone.Should().BeFalse();

        var completions = await verify.Set<RoutinePeriodCompletion>().AsNoTracking()
            .Where(c => c.TimePeriodId == period.Id)
            .ToListAsync(CancellationToken);
        completions.Should().NotBeEmpty();
    }

    // ---- CQ-8: dormancy is caught up in one sweep ------------------------------------------------------

    /// <summary>
    /// One <c>TryReset</c> advances the period by a single boundary, so a period nobody opened for a month used
    /// to need a month of nightly runs to become current — dripping one retroactive miss per night. One sweep
    /// must leave it current, with a row per closed cycle.
    /// </summary>
    [Fact]
    public async Task ResetSweep_PeriodDormantForSeveralCycles_ClosesThemAllInOneRun()
    {
        RoutineTimePeriod period;
        var lastResetAt = DateTime.UtcNow.AddDays(-10);
        await using (var db = CreateDbContext())
        {
            period = await SeedPeriodAsync(db, lengthInDays: 3, lastResetAt: lastResetAt, ct: CancellationToken);
            await SeedItemAsync(db, period, UserId, isDone: false, CancellationToken);
        }

        await RunJobAsync();

        await using var verify = CreateDbContext();
        var stored = await verify.Set<RoutineTimePeriod>().AsNoTracking()
            .FirstAsync(p => p.Id == period.Id, CancellationToken);
        // Boundaries at lastReset + 3/6/9 days (each truncated to midnight); + 12 is still in the future.
        stored.LastResetAt.Should().Be(lastResetAt.AddDays(9).Date);

        var completions = await verify.Set<RoutinePeriodCompletion>().AsNoTracking()
            .Where(c => c.TimePeriodId == period.Id)
            .ToListAsync(CancellationToken);
        completions.Should().HaveCount(3, "one row per closed cycle, so the heatmap shows the gap");
    }

    // ---- streak outcome branches ---------------------------------------------------------------------

    [Fact]
    public async Task ResetSweep_AllItemsDoneAboveThreshold_ExtendsTheStreak()
    {
        RoutineTimePeriod period;
        await using (var db = CreateDbContext())
        {
            period = await SeedPeriodAsync(db, lastResetAt: DateTime.UtcNow.AddDays(-8), streak: 2, bestStreak: 2, ct: CancellationToken);
            await SeedItemAsync(db, period, UserId, isDone: true, CancellationToken);
        }

        await RunJobAsync();

        await using var verify = CreateDbContext();
        var stored = await verify.Set<RoutineTimePeriod>().AsNoTracking()
            .FirstAsync(p => p.Id == period.Id, CancellationToken);
        stored.Streak.Should().Be(3);
        stored.BestStreak.Should().Be(3);
        stored.StreakGraceUntil.Should().BeNull();
    }

    [Fact]
    public async Task ResetSweep_BelowThresholdWithGraceDays_PutsThePeriodOnGrace()
    {
        RoutineTimePeriod period;
        await using (var db = CreateDbContext())
        {
            period = await SeedPeriodAsync(
                db, lastResetAt: DateTime.UtcNow.AddDays(-8), streakThreshold: 50, streakGraceDays: 3, streak: 2, ct: CancellationToken);
            await SeedItemAsync(db, period, UserId, isDone: false, CancellationToken);
        }

        await RunJobAsync();

        await using var verify = CreateDbContext();
        var stored = await verify.Set<RoutineTimePeriod>().AsNoTracking()
            .FirstAsync(p => p.Id == period.Id, CancellationToken);
        stored.Streak.Should().Be(2, "grace holds the streak pending expiry rather than breaking it immediately");
        stored.StreakGraceUntil.Should().NotBeNull();
    }

    [Fact]
    public async Task ResetSweep_BelowThresholdWithNoGrace_BreaksTheStreak()
    {
        RoutineTimePeriod period;
        await using (var db = CreateDbContext())
        {
            period = await SeedPeriodAsync(
                db, lastResetAt: DateTime.UtcNow.AddDays(-8), streakThreshold: 50, streakGraceDays: 0, streak: 4, ct: CancellationToken);
            await SeedItemAsync(db, period, UserId, isDone: false, CancellationToken);
        }

        await RunJobAsync();

        await using var verify = CreateDbContext();
        var stored = await verify.Set<RoutineTimePeriod>().AsNoTracking()
            .FirstAsync(p => p.Id == period.Id, CancellationToken);
        stored.Streak.Should().Be(0);
        stored.StreakGraceUntil.Should().BeNull();
    }

    /// <summary>An empty period is the easiest branch to get wrong by treating 0/0 as 0% and breaking the streak.</summary>
    [Fact]
    public async Task ResetSweep_EmptyPeriod_LeavesTheStreakUntouched()
    {
        RoutineTimePeriod period;
        await using (var db = CreateDbContext())
        {
            period = await SeedPeriodAsync(db, lastResetAt: DateTime.UtcNow.AddDays(-8), streak: 3, bestStreak: 3, ct: CancellationToken);
        }

        await RunJobAsync();

        await using var verify = CreateDbContext();
        var stored = await verify.Set<RoutineTimePeriod>().AsNoTracking()
            .FirstAsync(p => p.Id == period.Id, CancellationToken);
        stored.Streak.Should().Be(3);
        stored.BestStreak.Should().Be(3);

        var completion = await verify.Set<RoutinePeriodCompletion>().AsNoTracking()
            .SingleAsync(c => c.TimePeriodId == period.Id, CancellationToken);
        completion.TotalCount.Should().Be(0);
    }

    // ---- notification ordering ------------------------------------------------------------------------

    /// <summary>
    /// The summary notification is sent after the commit specifically so a notifier failure cannot roll the
    /// reset back. The real notifier implementation swallows its own exceptions, but the handler itself does
    /// not wrap the call in a try/catch — it trusts that contract — so a notifier that breaks it is expected
    /// to propagate. What matters is that the reset already landed by then.
    /// </summary>
    [Fact]
    public async Task ResetSweep_PersistsTheReset_EvenWhenTheNotifierThrows()
    {
        RoutineTimePeriod period;
        RoutineTodoList item;
        await using (var db = CreateDbContext())
        {
            period = await SeedPeriodAsync(db, lastResetAt: DateTime.UtcNow.AddDays(-8), ct: CancellationToken);
            item = await SeedItemAsync(db, period, UserId, isDone: true, CancellationToken);
        }

        var throwingNotifier = new Mock<IRoutinePeriodNotificationService>();
        throwingNotifier
            .Setup(n => n.NotifyPeriodEndedAsync(
                It.IsAny<RoutineTimePeriod>(), It.IsAny<RoutinePeriodCompletion>(), It.IsAny<StreakOutcome>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("notifier boom"));

        await using var factory = Fixture.CreateFactory(roles: null, configureServices: services =>
        {
            services.RemoveAll<IRoutinePeriodNotificationService>();
            services.AddScoped(_ => throwingNotifier.Object);
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => RunJobAsync(factory.Services));

        await using var verify = CreateDbContext();
        var storedItem = await verify.Set<RoutineTodoList>().AsNoTracking()
            .FirstAsync(i => i.Id == item.Id, CancellationToken);
        storedItem.IsDone.Should().BeFalse();

        var completion = await verify.Set<RoutinePeriodCompletion>().AsNoTracking()
            .SingleOrDefaultAsync(c => c.TimePeriodId == period.Id, CancellationToken);
        completion.Should().NotBeNull();
    }

    // ---- scope: the sweep is deliberately not user-scoped ----------------------------------------------

    /// <summary>
    /// The job runs unauthenticated and sweeps every user's periods — the global <c>IEntityWithUser</c> filter
    /// degenerates to "let everything through" outside a request. This pins that intended behavior; it is not
    /// something a future change should "fix" into a per-user sweep.
    /// </summary>
    [Fact]
    public async Task ResetSweep_ResetsPeriodsForEveryUser_NotJustTheAmbientOne()
    {
        RoutineTimePeriod mine, theirs;
        await using (var db = CreateDbContext())
        {
            await ReminderSeedHelper.EnsureOtherUserAsync(db, CancellationToken);

            mine = await SeedPeriodAsync(db, userId: UserId, lastResetAt: DateTime.UtcNow.AddDays(-8), ct: CancellationToken);
            await SeedItemAsync(db, mine, UserId, isDone: true, CancellationToken);

            theirs = await SeedPeriodAsync(db, userId: ReminderSeedHelper.OtherUserId, lastResetAt: DateTime.UtcNow.AddDays(-8), ct: CancellationToken);
            await SeedItemAsync(db, theirs, ReminderSeedHelper.OtherUserId, isDone: true, CancellationToken);
        }

        // The test factory's FakeLoggedUserService hard-codes IsAuthenticated => true (see its own doc
        // comment), which would scope AppDbContext.ScopeUserId to the factory's default user and defeat
        // the point of this test. Swap in a stub that behaves the way the real, HttpContext-backed
        // ILoggedUserService does with no request in flight — IsAuthenticated => false — which is what
        // actually makes the global filter inert for the nightly sweep in production.
        await using var factory = Fixture.CreateFactory(roles: null, configureServices: services =>
        {
            services.RemoveAll<ILoggedUserService>();
            services.AddScoped<ILoggedUserService>(_ => new UnauthenticatedLoggedUserService());
        });

        await RunJobAsync(factory.Services);

        await using var verify = CreateDbContext();
        var periods = await verify.Set<RoutineTimePeriod>().AsNoTracking()
            .Where(p => p.Id == mine.Id || p.Id == theirs.Id)
            .ToListAsync(CancellationToken);

        periods.Should().HaveCount(2).And.OnlyContain(p => p.Streak == 1);
    }

    /// <summary>
    /// PERF-3. The sweep now loads a page of users at a time and clears the change tracker at each page
    /// boundary, which is exactly the shape that can silently stop halfway: a clear that lands while later
    /// periods are still tracked detaches them and their saves write nothing, with no error and no log line.
    /// Page size 1 forces a boundary between the two users; the second user's period carries the assertion.
    /// <para>
    /// The first user holds two periods so the other half of the rule is pinned too — a page is a set of
    /// users, not of rows, so one user's periods can never be split across two pages and half-swept.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ResetSweep_AcrossPageBoundaries_ResetsEveryUsersPeriods()
    {
        RoutineTimePeriod weekly, fortnightly, theirs;
        await using (var db = CreateDbContext())
        {
            await ReminderSeedHelper.EnsureOtherUserAsync(db, CancellationToken);

            weekly = await SeedPeriodAsync(db, userId: UserId, lastResetAt: DateTime.UtcNow.AddDays(-8), ct: CancellationToken);
            await SeedItemAsync(db, weekly, UserId, isDone: true, CancellationToken);

            // Same user, second period — unique (user_id, length_in_days) forces a different length.
            fortnightly = await SeedPeriodAsync(db, userId: UserId, lengthInDays: 14,
                lastResetAt: DateTime.UtcNow.AddDays(-15), ct: CancellationToken);
            await SeedItemAsync(db, fortnightly, UserId, isDone: true, CancellationToken);

            theirs = await SeedPeriodAsync(db, userId: ReminderSeedHelper.OtherUserId,
                lastResetAt: DateTime.UtcNow.AddDays(-8), ct: CancellationToken);
            await SeedItemAsync(db, theirs, ReminderSeedHelper.OtherUserId, isDone: true, CancellationToken);
        }

        await using var factory = Fixture.CreateFactory(roles: null, configureServices: services =>
        {
            services.RemoveAll<ILoggedUserService>();
            services.AddScoped<ILoggedUserService>(_ => new UnauthenticatedLoggedUserService());
        });

        await RunJobAsync(factory.Services, userPageSize: 1);

        await using var verify = CreateDbContext();
        var ids = new[] { weekly.Id, fortnightly.Id, theirs.Id };

        var periods = await verify.Set<RoutineTimePeriod>().AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .ToListAsync(CancellationToken);

        periods.Should().HaveCount(3).And.OnlyContain(p => p.Streak == 1,
            "every page's periods must be committed, not just the last one's");

        // On the rows, not on the tracker: a detached period's item update is the silent half of this failure.
        var items = await verify.Set<RoutineTodoList>().AsNoTracking()
            .Where(i => ids.Contains(i.TimePeriodId))
            .ToListAsync(CancellationToken);

        items.Should().HaveCount(3).And.OnlyContain(i => !i.IsDone);

        var completions = await verify.Set<RoutinePeriodCompletion>().AsNoTracking()
            .Where(c => ids.Contains(c.TimePeriodId))
            .ToListAsync(CancellationToken);

        completions.Select(c => c.TimePeriodId).Should().BeEquivalentTo(ids,
            "each period closes exactly one cycle here, and the completion row is what the history reads");
    }

    private sealed class UnauthenticatedLoggedUserService : ILoggedUserService
    {
        public ClaimsPrincipal? LoggedUserPrincipal => null;
        public bool IsAuthenticated => false;
        public IEnumerable<string> GetRoles => [];
        public string GetEmail => throw new InvalidOperationException("Not authenticated");
        public long GetUserId => throw new InvalidOperationException("Not authenticated");
        public long? GetUserIdOrNull => null;
    }
}
