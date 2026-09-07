using System.Security.Claims;
using AdhdTimeOrganizer.IntegrationTests.Endpoints;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.IntegrationTests.Reminders;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.Routines.infrastructure.jobs;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sydowwe.Framework.Contracts.scheduling;
using Sydowwe.Framework.domain.extServiceContract.user;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Routines;

/// <summary>
/// SEC-1's <i>consequence</i>, pinned on rows rather than on the input that used to produce it: a
/// <see cref="RoutineTodoList"/> whose <c>UserId</c> is not its period's owner must not be scored into that
/// owner's streak.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Endpoints.RoutineTodoListPeriodRulesTests"/> covers the ingress — the validator now refuses a
/// <c>TimePeriodId</c> the caller does not own, on both write endpoints. That closes the door; it does not
/// clean up behind it. This suite inserts the cross-tenant row <b>directly through the DbContext</b>,
/// deliberately bypassing the endpoint, so it keeps testing the thing that actually hurts the victim
/// whatever shape the input validation takes — and so it still speaks for rows written before the guard
/// existed.
/// </para>
/// <para>
/// The damage path is <see cref="RoutineTodoListResetJobHandler"/>: it runs unauthenticated, which is
/// exactly when the host's per-user query filter goes inert, so an unfiltered
/// <c>Include(tp =&gt; tp.RoutineTodoListColl)</c> loads every row pointing at the period and divides by it
/// in <c>RoutineResetService.TryReset</c>'s <c>completedCount / items.Count</c>. A stranger's permanently
/// unticked item halves the victim's completion rate, breaks a streak the victim cannot see the cause of,
/// and writes a <see cref="RoutinePeriodCompletion"/> row that is wrong for good — the ledger is
/// append-only. The include is now filtered on the period's owner; this test is what holds it there, and it
/// fails against the unfiltered version.
/// </para>
/// </remarks>
[Collection("Postgres")]
public class RoutineForeignPeriodStreakTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long VictimUserId = FakeLoggedUserService.TestUserId;
    private static long AttackerUserId => ReminderSeedHelper.OtherUserId;

    /// <summary>
    /// The victim's period holds one item, their own, and it is done — so on its own the period is at 100%
    /// against a threshold of 100 and the streak must advance. The only other row pointing at the period
    /// belongs to somebody else, so the sweep must not see it: <c>Streak == 1</c> and a completion row
    /// counting <b>one</b> item, not two.
    /// </summary>
    [Fact(DisplayName = "SEC-1: an item owned by another user is not counted against its period owner's streak")]
    public async Task ForeignTimePeriod_IsNotCountedAgainstTheVictimsStreak()
    {
        RoutineTimePeriod period;
        await using (var db = CreateDbContext())
        {
            // The attacker must be a real row: routine_todo_list.user_id is a NOT NULL FK.
            await ReminderSeedHelper.EnsureOtherUserAsync(db, CancellationToken);

            // Threshold 100 and no grace: with only the victim's own done item in scope this is an unambiguous
            // Extended; one uncounted foreign item is enough to flip it to Broken, so the assertion has teeth
            // in exactly one direction.
            period = new RoutineTimePeriod
            {
                UserId = VictimUserId,
                Text = $"Period {Guid.NewGuid():N}",
                Color = "#123456",
                LengthInDays = 7,
                // Rolling anchor: the reset is due purely on LastResetAt + LengthInDays, so this is due on any
                // calendar day the suite runs.
                ResetAnchorDay = 0,
                StreakThreshold = 100,
                StreakGraceDays = 0,
                Streak = 0,
                BestStreak = 0,
                LastResetAt = DateTime.UtcNow.AddDays(-8)
            };
            db.Set<RoutineTimePeriod>().Add(period);
            await db.SaveChangesAsync(CancellationToken);

            var victimActivityId = await PlanningTestSeedHelper.SeedActivityAsync(
                db, "Victim routine", VictimUserId, CancellationToken);
            await TodoListTestSeedHelper.SeedRoutineTodoListAsync(
                db, victimActivityId, period.Id, VictimUserId, isDone: true, ct: CancellationToken);

            // The cross-tenant row: the attacker's own UserId and activity, pointed at the victim's period.
            // Written straight to the database because the endpoint no longer accepts it.
            var attackerActivityId = await PlanningTestSeedHelper.SeedActivityAsync(
                db, "Attacker routine", AttackerUserId, CancellationToken);
            await TodoListTestSeedHelper.SeedRoutineTodoListAsync(
                db, attackerActivityId, period.Id, AttackerUserId, isDone: false, ct: CancellationToken);
        }

        // FakeLoggedUserService hard-codes IsAuthenticated => true, which would scope the sweep to the default
        // user and hide the whole failure mode. The stub below is how the real HttpContext-backed service
        // behaves with no request in flight, which is what makes the filter inert for the nightly job.
        await using var factory = Fixture.CreateFactory(roles: null, configureServices: services =>
        {
            services.RemoveAll<ILoggedUserService>();
            services.AddScoped<ILoggedUserService>(_ => new UnauthenticatedLoggedUserService());
        });

        await RunJobAsync(factory.Services);

        await using var verify = CreateDbContext();

        var stored = await verify.Set<RoutineTimePeriod>().AsNoTracking()
            .FirstAsync(p => p.Id == period.Id, CancellationToken);
        stored.Streak.Should().Be(1,
            "the victim completed every item that is actually theirs, so a stranger's row must not break the streak");

        var completion = await verify.Set<RoutinePeriodCompletion>().AsNoTracking()
            .IgnoreQueryFilters()
            .SingleAsync(c => c.TimePeriodId == period.Id, CancellationToken);
        completion.TotalCount.Should().Be(1, "the ledger is append-only, so a wrong denominator here is permanent");
        completion.CompletedCount.Should().Be(1);
    }

    /// <summary>
    /// Runs the handler the way the Scheduler's dispatcher would — one DI scope, a hand-built context the
    /// handler reads nothing off. Mirrors <c>RoutineTodoListResetJobHandlerTests.RunJobAsync</c>.
    /// </summary>
    private async Task RunJobAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var handler = ActivatorUtilities.CreateInstance<RoutineTodoListResetJobHandler>(scope.ServiceProvider);

        var now = DateTime.UtcNow;
        await handler.ExecuteAsync(new ScheduledJobContext
        {
            ScheduledFireTimeUtc = now,
            ActualFireTimeUtc = now,
            JobKey = handler.Key,
            CorrelationId = "routine-foreign-period-streak-test",
            TriggerSource = TriggerSource.Manual
        }, CancellationToken);
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
