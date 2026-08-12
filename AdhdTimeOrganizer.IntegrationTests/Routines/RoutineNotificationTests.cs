using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.Routines.infrastructure.jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sydowwe.Framework.Contracts.notification;
using Sydowwe.Framework.Contracts.scheduling;
using Sydowwe.Framework.Testing;
using Sydowwe.Notifications.domain.entity;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Routines;

/// <summary>
/// The routine domain's notification producers, end to end: a real period in a real database, the real job,
/// and a notification row at the other end.
/// <para>
/// These exist because "the module is wired but nothing produces for it" is invisible to every other kind of
/// test — the endpoints answer, the seams resolve, and the bell stays empty forever. Each test asserts the
/// row lands <b>and</b> that a second sweep does not duplicate it, since the idempotency mark is the only
/// thing standing between a daily sweep and a daily duplicate.
/// </para>
/// </summary>
[Collection("Postgres")]
public class RoutineNotificationTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    // ---- helpers ---------------------------------------------------------------------------------

    private static async Task<RoutineTimePeriod> SeedPeriodAsync(
        DbContext db,
        int lengthInDays = 7,
        int? reminderLeadDays = null,
        DateTime? lastResetAt = null,
        int streak = 0,
        int graceDays = 0,
        CancellationToken ct = default)
    {
        var period = new RoutineTimePeriod
        {
            UserId = UserId,
            // Unique per test: routine_time_period has unique indexes on (user_id, text) and (user_id, length_in_days).
            Text = $"Period {Guid.NewGuid():N}",
            Color = "#123456",
            LengthInDays = lengthInDays,
            ResetAnchorDay = 0,
            StreakThreshold = 100,
            StreakGraceDays = graceDays,
            ReminderLeadDays = reminderLeadDays,
            Streak = streak,
            LastResetAt = lastResetAt
        };
        db.Set<RoutineTimePeriod>().Add(period);
        await db.SaveChangesAsync(ct);
        return period;
    }

    private static async Task SeedItemAsync(DbContext db, RoutineTimePeriod period, bool isDone, CancellationToken ct)
    {
        var role = new ActivityRole { UserId = UserId, Name = $"Role {Guid.NewGuid():N}", Color = "#123456" };
        db.Set<ActivityRole>().Add(role);
        await db.SaveChangesAsync(ct);

        var activity = new Activity { UserId = UserId, Name = $"Activity {Guid.NewGuid():N}", RoleId = role.Id };
        db.Set<Activity>().Add(activity);
        await db.SaveChangesAsync(ct);

        db.Set<RoutineTodoList>().Add(new RoutineTodoList
        {
            UserId = UserId,
            ActivityId = activity.Id,
            TimePeriodId = period.Id,
            IsDone = isDone
        });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Runs a scheduled job the way the Scheduler's dispatcher would: one DI scope per fire (the handlers
    /// take a scoped <c>DbContext</c>), and a hand-built <see cref="ScheduledJobContext"/> — these handlers
    /// read nothing off it, all their state comes from the database.
    /// </summary>
    private async Task RunJobAsync<THandler>() where THandler : IScheduledJobHandler
    {
        await using var scope = Fixture.AdminAndUserFactory.Services.CreateAsyncScope();
        var handler = (IScheduledJobHandler)ActivatorUtilities.CreateInstance(scope.ServiceProvider, typeof(THandler));

        var now = DateTime.UtcNow;
        await handler.ExecuteAsync(new ScheduledJobContext
        {
            ScheduledFireTimeUtc = now,
            ActualFireTimeUtc = now,
            JobKey = handler.Key,
            CorrelationId = "routine-notification-test",
            TriggerSource = TriggerSource.Manual
        }, CancellationToken);
    }

    private async Task<int> CountNotificationsAsync(NotificationType type)
    {
        await using var db = CreateDbContext();
        return await db.Set<Notification>().AsNoTracking()
            .CountAsync(n => n.UserId == UserId && n.Type == type, CancellationToken);
    }

    // ---- lead-time nudge -------------------------------------------------------------------------

    /// <summary>
    /// The whole point of the feature: a period inside its lead window with work left produces a notification.
    /// The second sweep is the other half — this job runs every day, so without the mark the user would get the
    /// same nudge every morning until the period reset.
    /// </summary>
    [Fact]
    public async Task NudgeSweep_RaisesEndingSoon_AndDoesNotRepeatIt()
    {
        RoutineTimePeriod period;
        await using (var db = CreateDbContext())
        {
            // Reset lands two days out; a three-day lead therefore has the window already open.
            period = await SeedPeriodAsync(db, reminderLeadDays: 3, lastResetAt: DateTime.UtcNow.AddDays(-5), ct: CancellationToken);
            await SeedItemAsync(db, period, isDone: false, CancellationToken);
        }

        await RunJobAsync<RoutinePeriodNudgeJobHandler>();
        await RunJobAsync<RoutinePeriodNudgeJobHandler>();

        Assert.Equal(1, await CountNotificationsAsync(NotificationType.RoutinePeriodEndingSoon));

        await using var verify = CreateDbContext();
        var stored = await verify.Set<RoutineTimePeriod>().AsNoTracking()
            .FirstAsync(p => p.Id == period.Id, CancellationToken);
        Assert.NotNull(stored.EndingSoonNotifiedFor);
    }

    /// <summary>
    /// Nothing outstanding is not a notification — and the period is left <b>unmarked</b>, which is what lets
    /// un-ticking an item tomorrow still earn the nudge. Marking it here would silently spend the cycle.
    /// </summary>
    [Fact]
    public async Task NudgeSweep_AllItemsDone_SendsNothingAndLeavesThePeriodUnmarked()
    {
        RoutineTimePeriod period;
        await using (var db = CreateDbContext())
        {
            period = await SeedPeriodAsync(db, reminderLeadDays: 3, lastResetAt: DateTime.UtcNow.AddDays(-5), ct: CancellationToken);
            await SeedItemAsync(db, period, isDone: true, CancellationToken);
        }

        await RunJobAsync<RoutinePeriodNudgeJobHandler>();

        Assert.Equal(0, await CountNotificationsAsync(NotificationType.RoutinePeriodEndingSoon));

        await using var verify = CreateDbContext();
        var stored = await verify.Set<RoutineTimePeriod>().AsNoTracking()
            .FirstAsync(p => p.Id == period.Id, CancellationToken);
        Assert.Null(stored.EndingSoonNotifiedFor);
    }

    /// <summary>Null <c>ReminderLeadDays</c> is the opt-out, and it is the default every existing period has.</summary>
    [Fact]
    public async Task NudgeSweep_NoLeadConfigured_SendsNothing()
    {
        await using (var db = CreateDbContext())
        {
            var period = await SeedPeriodAsync(db, lastResetAt: DateTime.UtcNow.AddDays(-5), ct: CancellationToken);
            await SeedItemAsync(db, period, isDone: false, CancellationToken);
        }

        await RunJobAsync<RoutinePeriodNudgeJobHandler>();

        Assert.Equal(0, await CountNotificationsAsync(NotificationType.RoutinePeriodEndingSoon));
    }

    // ---- grace warning ---------------------------------------------------------------------------

    [Fact]
    public async Task NudgeSweep_RaisesGraceWarning_AndDoesNotRepeatIt()
    {
        RoutineTimePeriod period;
        await using (var db = CreateDbContext())
        {
            period = await SeedPeriodAsync(db, streak: 4, graceDays: 3, lastResetAt: DateTime.UtcNow, ct: CancellationToken);
            period.StreakGraceUntil = DateTime.UtcNow.AddHours(12);
            await db.SaveChangesAsync(CancellationToken);
        }

        await RunJobAsync<RoutinePeriodNudgeJobHandler>();
        await RunJobAsync<RoutinePeriodNudgeJobHandler>();

        Assert.Equal(1, await CountNotificationsAsync(NotificationType.RoutineStreakGraceExpiring));

        await using var verify = CreateDbContext();
        var stored = await verify.Set<RoutineTimePeriod>().AsNoTracking()
            .FirstAsync(p => p.Id == period.Id, CancellationToken);
        Assert.NotNull(stored.GraceNotifiedFor);
    }

    // ---- end-of-period summary -------------------------------------------------------------------

    /// <summary>
    /// The reactive half. The reset job already existed and already did all of this silently; the assertion is
    /// that it now says so.
    /// </summary>
    [Fact]
    public async Task ResetJob_RaisesPeriodEndedSummary()
    {
        await using (var db = CreateDbContext())
        {
            // Elapsed: an 8-day-old reset on a 7-day period is due.
            var period = await SeedPeriodAsync(db, lastResetAt: DateTime.UtcNow.AddDays(-8), ct: CancellationToken);
            await SeedItemAsync(db, period, isDone: false, CancellationToken);
        }

        await RunJobAsync<RoutineTodoListResetJobHandler>();

        Assert.Equal(1, await CountNotificationsAsync(NotificationType.RoutinePeriodEnded));
    }
}