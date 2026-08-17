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
            ResetAnchorDay = 0,
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
    private async Task RunJobAsync(IServiceProvider? services = null)
    {
        await using var scope = (services ?? Fixture.AdminAndUserFactory.Services).CreateAsyncScope();
        var handler = ActivatorUtilities.CreateInstance<RoutineTodoListResetJobHandler>(scope.ServiceProvider);

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
