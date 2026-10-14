using System.Security.Claims;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.IntegrationTests.Endpoints;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.IntegrationTests.Reminders;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.Routines.domain.model.@enum;
using AdhdTimeOrganizer.Routines.domain.service;
using AdhdTimeOrganizer.Routines.domain.serviceContract;
using AdhdTimeOrganizer.Routines.infrastructure.jobs;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sydowwe.Framework.Contracts.scheduling;
using Sydowwe.Framework.domain.extServiceContract.user;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Routines;

/// <summary>
/// TEST-13 — <c>RoutinePeriodNudgeJobHandler</c>, substituting <see cref="IRoutinePeriodNotificationService"/>
/// with a controllable test double so behaviour can be pinned on exactly what the job passed the notifier,
/// independent of the real notification pipeline already covered by <c>RoutineNotificationTests</c>.
/// <para>
/// CQ-5 (Scenario A below) turned out to already be guarded in current <c>main</c>: the handler wraps each
/// period's notify calls in a per-period try/catch and does a single <c>SaveChangesAsync</c> after the loop, so
/// an earlier period's tracked marker survives a later period's throw. The scenario is kept (unmarked, no
/// <c>KnownGap</c> trait) as the regression pin for that guarantee.
/// </para>
/// <para>
/// <b>The clock is pinned, years in the past.</b> Delivery waits for each user's own morning, so a case read
/// off the wall clock would pass or fail by the hour it ran at. And Quartz fires this job every quarter hour
/// inside every test host against the shared database with the <i>real</i> clock — seeded around the real
/// today, such a fire could land mid-test and mark the very period a case is about. Every period here resets
/// in 2020, so a real fire finds every window long closed. Same reasoning as <c>RoutineSuggestedTodayJobTests</c>.
/// </para>
/// </summary>
[Collection("Postgres")]
public class RoutinePeriodNudgeJobTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    /// <summary>
    /// Tuesday 29 September 2020, 12:00 UTC — past the default morning (08:00 + 1 h) of every seeded user, all
    /// of them UTC unless a case says otherwise.
    /// </summary>
    private static readonly DateTime Now = new(2020, 9, 29, 12, 0, 0, DateTimeKind.Utc);

    private static readonly TimeZoneInfo Tokyo = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");
    private static readonly TimeZoneInfo Bratislava = TimeZoneInfo.FindSystemTimeZoneById("Europe/Bratislava");
    private static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    private sealed class FixedTimeProvider(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now, TimeSpan.Zero);
    }

    // ---- test double -------------------------------------------------------------------------------

    private sealed class RoutinePeriodNotificationServiceDouble : IRoutinePeriodNotificationService
    {
        public sealed record EndingSoonCall(long PeriodId, long UserId, int Remaining, int Total, int DaysLeft);
        public sealed record GraceCall(long PeriodId, long UserId);

        public List<EndingSoonCall> EndingSoonCalls { get; } = [];
        public List<GraceCall> GraceCalls { get; } = [];

        /// <summary>1-based ordinal across both notify methods, in call order. 0 = never throw.</summary>
        public int ThrowOnCallNumber { get; set; }

        /// <summary>
        /// Mirrors the real implementation swallowing a dispatch failure: the call is recorded and returns
        /// false instead of throwing, which is the only signal the job gets that nothing went out.
        /// </summary>
        public bool GraceDispatchFails { get; set; }

        private int _callCount;

        public Task NotifyPeriodEndedAsync(RoutineTimePeriod period, RoutineResetService.RoutinePeriodReset reset, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<bool> NotifyEndingSoonAsync(RoutineTimePeriod period, RoutineResetService.RoutineNudge nudge, int remaining, int total, CancellationToken ct = default)
        {
            // Mirrors the real implementation's "nothing outstanding is not a notification" branch, since the
            // job relies on that contract (the false return is what keeps the period unmarked).
            if (remaining <= 0)
                return Task.FromResult(false);

            _callCount++;
            EndingSoonCalls.Add(new EndingSoonCall(period.Id, period.UserId, remaining, total, nudge.DaysLeft));
            if (_callCount == ThrowOnCallNumber)
                throw new InvalidOperationException($"stub notifier failure on call {_callCount}");

            return Task.FromResult(true);
        }

        public Task<bool> NotifyGraceExpiringAsync(RoutineTimePeriod period, CancellationToken ct = default)
        {
            _callCount++;
            GraceCalls.Add(new GraceCall(period.Id, period.UserId));
            if (_callCount == ThrowOnCallNumber)
                throw new InvalidOperationException($"stub notifier failure on call {_callCount}");

            return Task.FromResult(!GraceDispatchFails);
        }

        public Task<bool> NotifySuggestedTodayAsync(long userId, DateOnly date, IReadOnlyList<RoutineTodoList> items, CancellationToken ct = default)
            => Task.FromResult(false);
    }

    /// <summary>
    /// Mirrors the real <c>LoggedUserService</c>'s shape with no ambient <c>HttpContext</c> (background job
    /// scope: <c>GetUserIdOrNull</c> is null, so <c>AppDbContext</c>'s per-user query filter is inert).
    /// <see cref="FakeLoggedUserService"/> cannot stand in for this: with no ambient <c>HttpContext</c> it falls
    /// back to a fixed <c>userId</c> instead of returning null, which would silently scope the job's DbContext
    /// to one user -- exactly the behavior these cross-user tests exist to rule out.
    /// </summary>
    private sealed class NoAmbientUserLoggedUserService : ILoggedUserService
    {
        public ClaimsPrincipal? LoggedUserPrincipal => null;
        public bool IsAuthenticated => false;
        public IEnumerable<string> GetRoles => [];
        public string GetEmail => throw new InvalidOperationException("No ambient user in a background job scope.");
        public long GetUserId => throw new InvalidOperationException("No ambient user in a background job scope.");
        public long? GetUserIdOrNull => null;
    }

    // ---- helpers ------------------------------------------------------------------------------------

    private static async Task<RoutineTimePeriod> SeedPeriodAsync(
        DbContext db,
        long userId,
        int lengthInDays = 7,
        int? reminderLeadDays = null,
        DateTime? lastResetAt = null,
        bool isHidden = false,
        int graceDays = 0,
        DateTime? streakGraceUntil = null,
        DateTime? graceNotifiedFor = null,
        DateTime? endingSoonNotifiedFor = null,
        CancellationToken ct = default)
    {
        var period = new RoutineTimePeriod
        {
            UserId = userId,
            // Unique per test: routine_time_period has unique indexes on (user_id, text) and (user_id, length_in_days).
            Text = $"Nudge period {Guid.NewGuid():N}",
            Color = "#123456",
            LengthInDays = lengthInDays,
            ResetAnchorDay = 0,
            StreakThreshold = 100,
            StreakGraceDays = graceDays,
            ReminderLeadDays = reminderLeadDays,
            LastResetAt = lastResetAt,
            IsHidden = isHidden,
            StreakGraceUntil = streakGraceUntil,
            GraceNotifiedFor = graceNotifiedFor,
            EndingSoonNotifiedFor = endingSoonNotifiedFor
        };
        db.Set<RoutineTimePeriod>().Add(period);
        await db.SaveChangesAsync(ct);
        return period;
    }

    private static async Task<RoutineTodoList> SeedItemAsync(DbContext db, RoutineTimePeriod period, bool isDone, long userId, CancellationToken ct)
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
        db.Set<RoutineTodoList>().Add(item);
        await db.SaveChangesAsync(ct);
        return item;
    }

    /// <summary>A second/third/... real user row (the UserId FK is NOT NULL), so cross-user tests have someone to seed as.</summary>
    private static async Task EnsureUserAsync(DbContext db, long userId, TimeZoneInfo? timezone, CancellationToken ct)
    {
        if (await db.Set<User>().FindAsync([userId], ct) is not null)
            return;

        var email = $"nudge-{userId}@test.com";
        var user = new User
        {
            Id = userId,
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N"),
            Timezone = timezone ?? TimeZoneInfo.Utc
        };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, AppDbContextFixture.TestUserPassword);
        db.Set<User>().Add(user);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Runs the job the way the Scheduler's dispatcher would (one DI scope per fire), against a factory whose
    /// <see cref="IRoutinePeriodNotificationService"/> has been substituted with a controllable double.
    /// </summary>
    private static async Task RunJobAsync(IServiceProvider services, DateTime now, CancellationToken ct, int? userPageSize = null)
    {
        await using var scope = services.CreateAsyncScope();
        // PERF-4: the sweep pages by user. Passing 1 puts every user on their own page, which is the only way
        // to cross a page boundary without seeding RoutineSweepPaging.UserPageSize users; omitting it takes
        // the constructor default, which is what DI and the dispatcher do.
        var handler = (IScheduledJobHandler)(userPageSize is { } size
            ? ActivatorUtilities.CreateInstance(scope.ServiceProvider, typeof(RoutinePeriodNudgeJobHandler), size)
            : ActivatorUtilities.CreateInstance(scope.ServiceProvider, typeof(RoutinePeriodNudgeJobHandler)));

        await handler.ExecuteAsync(new ScheduledJobContext
        {
            ScheduledFireTimeUtc = now,
            ActualFireTimeUtc = now,
            JobKey = handler.Key,
            CorrelationId = "routine-nudge-job-test",
            TriggerSource = TriggerSource.Manual
        }, ct);
    }

    private async Task RunJobWithDoubleAsync(
        RoutinePeriodNotificationServiceDouble notifier, CancellationToken ct, int? userPageSize = null, DateTime? at = null)
    {
        var now = at ?? Now;
        await using var factory = CreateFactory(TestRoles.AdminAndUser, configureServices: services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new FixedTimeProvider(now));

            services.RemoveAll<IRoutinePeriodNotificationService>();
            services.AddSingleton<IRoutinePeriodNotificationService>(notifier);

            // See NoAmbientUserLoggedUserService: without this override every query through this factory's
            // DbContext is silently scoped to the factory's default userId, which is not how the real
            // unauthenticated job scope behaves.
            services.RemoveAll<ILoggedUserService>();
            services.AddScoped<ILoggedUserService>(_ => new NoAmbientUserLoggedUserService());
        });
        await RunJobAsync(factory.Services, now, ct, userPageSize);
    }

    // ---- A. CQ-5: mid-loop notifier failure must not lose earlier markers ---------------------------

    [Fact]
    public async Task NudgeSweep_MidLoopNotifierFailure_PreservesEarlierMarkersAndStillProcessesLaterPeriods()
    {
        var thirdUserId = FakeLoggedUserService.TestUserId + 2;
        long period1Id, period2Id, period3Id;

        await using (var db = CreateDbContext())
        {
            await ReminderSeedHelper.EnsureOtherUserAsync(db, CancellationToken);
            await EnsureUserAsync(db, thirdUserId, null, CancellationToken);

            var period1 = await SeedPeriodAsync(db, UserId, reminderLeadDays: 3, lastResetAt: Now.AddDays(-5), ct: CancellationToken);
            await SeedItemAsync(db, period1, isDone: false, UserId, CancellationToken);
            period1Id = period1.Id;

            var period2 = await SeedPeriodAsync(db, ReminderSeedHelper.OtherUserId, reminderLeadDays: 3, lastResetAt: Now.AddDays(-5), ct: CancellationToken);
            await SeedItemAsync(db, period2, isDone: false, ReminderSeedHelper.OtherUserId, CancellationToken);
            period2Id = period2.Id;

            var period3 = await SeedPeriodAsync(db, thirdUserId, reminderLeadDays: 3, lastResetAt: Now.AddDays(-5), ct: CancellationToken);
            await SeedItemAsync(db, period3, isDone: false, thirdUserId, CancellationToken);
            period3Id = period3.Id;
        }

        // Succeeds for period 1 (call 1), throws for period 2 (call 2).
        var failingNotifier = new RoutinePeriodNotificationServiceDouble { ThrowOnCallNumber = 2 };
        await RunJobWithDoubleAsync(failingNotifier, CancellationToken);

        failingNotifier.EndingSoonCalls.Select(c => c.PeriodId).Should().BeEquivalentTo([period1Id, period2Id, period3Id],
            "all three periods must be attempted even though the second one threw");

        await using (var verify = CreateDbContext())
        {
            var stored = await verify.Set<RoutineTimePeriod>().AsNoTracking()
                .Where(p => p.Id == period1Id || p.Id == period2Id || p.Id == period3Id)
                .ToDictionaryAsync(p => p.Id, CancellationToken);

            stored[period1Id].EndingSoonNotifiedFor.Should().NotBeNull("period 1's successful notification must not be lost to period 2's later throw");
            stored[period2Id].EndingSoonNotifiedFor.Should().BeNull("period 2's notify call threw before it could be marked, so it must retry next run");
            stored[period3Id].EndingSoonNotifiedFor.Should().NotBeNull("a mid-loop failure must not abandon periods still to come");
        }

        // User-visible consequence: a second, healthy sweep must not re-notify period 1.
        var healthyNotifier = new RoutinePeriodNotificationServiceDouble();
        await RunJobWithDoubleAsync(healthyNotifier, CancellationToken);

        healthyNotifier.EndingSoonCalls.Select(c => c.PeriodId).Should().NotContain(period1Id, "period 1 was already notified in the first sweep");
        healthyNotifier.EndingSoonCalls.Select(c => c.PeriodId).Should().Contain(period2Id, "period 2's marker was never set, so it must retry");
    }

    // ---- FO1: away days are quiet days ------------------------------------------------------------------

    /// <summary>
    /// A user whose own today is a vacation day gets no lead-time nudge, and the period is left unmarked so the
    /// nudge can still go out once they are back. The second user, identical but at work, is the control: a
    /// guard that silenced the whole sweep would pass without them.
    /// </summary>
    [Fact(DisplayName = "FO1: no nudge on a user's away day, and nothing marked")]
    public async Task NudgeSweep_SkipsAUserWhoseTodayIsAway_WithoutMarking()
    {
        long awayPeriodId, homePeriodId;
        await using (var db = CreateDbContext())
        {
            await ReminderSeedHelper.EnsureOtherUserAsync(db, CancellationToken);

            var away = await SeedPeriodAsync(db, UserId, reminderLeadDays: 3, lastResetAt: Now.AddDays(-5), ct: CancellationToken);
            await SeedItemAsync(db, away, isDone: false, UserId, CancellationToken);
            awayPeriodId = away.Id;

            var home = await SeedPeriodAsync(db, ReminderSeedHelper.OtherUserId, reminderLeadDays: 3, lastResetAt: Now.AddDays(-5), ct: CancellationToken);
            await SeedItemAsync(db, home, isDone: false, ReminderSeedHelper.OtherUserId, CancellationToken);
            homePeriodId = home.Id;

            // Either side of the UTC date too, so the user's own today is away whatever their zone.
            var utcToday = DateOnly.FromDateTime(Now);
            for (var offset = -1; offset <= 1; offset++)
            {
                await PlanningTestSeedHelper.SeedCalendarAsync(
                    db, utcToday.AddDays(offset), UserId, CancellationToken,
                    AdhdTimeOrganizer.Core.domain.model.@enum.DayType.Vacation);
            }
        }

        var notifier = new RoutinePeriodNotificationServiceDouble();
        await RunJobWithDoubleAsync(notifier, CancellationToken);

        notifier.EndingSoonCalls.Select(c => c.PeriodId).Should().Equal(homePeriodId);

        await using var verify = CreateDbContext();
        var awayPeriod = await verify.Set<RoutineTimePeriod>().IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(p => p.Id == awayPeriodId, CancellationToken);
        awayPeriod.EndingSoonNotifiedFor.Should().BeNull("a skipped nudge is not a sent one, so it may still go out tomorrow");
    }

    // ---- B. Idempotent marking ------------------------------------------------------------------------

    [Fact]
    public async Task NudgeSweep_LeadTimeNudge_IsSentExactlyOnceAcrossTwoRuns()
    {
        await using (var db = CreateDbContext())
        {
            var period = await SeedPeriodAsync(db, UserId, reminderLeadDays: 3, lastResetAt: Now.AddDays(-5), ct: CancellationToken);
            await SeedItemAsync(db, period, isDone: false, UserId, CancellationToken);
        }

        var notifier = new RoutinePeriodNotificationServiceDouble();
        await RunJobWithDoubleAsync(notifier, CancellationToken);
        await RunJobWithDoubleAsync(notifier, CancellationToken);

        notifier.EndingSoonCalls.Should().HaveCount(1);
    }

    [Fact]
    public async Task NudgeSweep_GraceWarning_IsSentExactlyOnceAcrossTwoRuns()
    {
        await using (var db = CreateDbContext())
        {
            await SeedPeriodAsync(db, UserId, graceDays: 3, streakGraceUntil: Now.AddHours(12), ct: CancellationToken);
        }

        var notifier = new RoutinePeriodNotificationServiceDouble();
        await RunJobWithDoubleAsync(notifier, CancellationToken);
        await RunJobWithDoubleAsync(notifier, CancellationToken);

        notifier.GraceCalls.Should().HaveCount(1);
    }

    /// <summary>
    /// CQ-6 — a swallowed grace dispatch must not stamp <c>GraceNotifiedFor</c>. The marker is what
    /// <c>ShouldWarnGraceExpiring</c> reads, so stamping it on a failed send suppresses the warning for the
    /// rest of the window, which then lapses and breaks the streak with nothing ever having reached the user.
    /// </summary>
    [Fact]
    public async Task NudgeSweep_GraceWarning_SwallowedDispatch_LeavesPeriodUnmarkedAndRetriesNextRun()
    {
        long periodId;
        await using (var db = CreateDbContext())
        {
            var period = await SeedPeriodAsync(db, UserId, graceDays: 3, streakGraceUntil: Now.AddHours(12), ct: CancellationToken);
            periodId = period.Id;
        }

        var failingNotifier = new RoutinePeriodNotificationServiceDouble { GraceDispatchFails = true };
        await RunJobWithDoubleAsync(failingNotifier, CancellationToken);

        failingNotifier.GraceCalls.Select(c => c.PeriodId).Should().Equal([periodId]);

        await using (var verify = CreateDbContext())
        {
            var stored = await verify.Set<RoutineTimePeriod>().AsNoTracking().SingleAsync(p => p.Id == periodId, CancellationToken);
            stored.GraceNotifiedFor.Should().BeNull("nothing was delivered, so the warning must stay pending");
        }

        var healthyNotifier = new RoutinePeriodNotificationServiceDouble();
        await RunJobWithDoubleAsync(healthyNotifier, CancellationToken);

        healthyNotifier.GraceCalls.Select(c => c.PeriodId).Should().Equal([periodId], "the warning that failed must be retried");

        await using (var verify = CreateDbContext())
        {
            var stored = await verify.Set<RoutineTimePeriod>().AsNoTracking().SingleAsync(p => p.Id == periodId, CancellationToken);
            stored.GraceNotifiedFor.Should().NotBeNull("the retry went out, so the window is now marked");
        }
    }

    // ---- C. Fully-done periods are skipped without marking ---------------------------------------------

    [Fact]
    public async Task NudgeSweep_AllItemsDone_SkipsWithoutMarking_ThenNudgesOnceAnItemIsUnticked()
    {
        RoutineTimePeriod period;
        RoutineTodoList item;
        await using (var db = CreateDbContext())
        {
            period = await SeedPeriodAsync(db, UserId, reminderLeadDays: 3, lastResetAt: Now.AddDays(-5), ct: CancellationToken);
            item = await SeedItemAsync(db, period, isDone: true, UserId, CancellationToken);
        }

        var notifier = new RoutinePeriodNotificationServiceDouble();
        await RunJobWithDoubleAsync(notifier, CancellationToken);

        notifier.EndingSoonCalls.Should().BeEmpty();
        await using (var verify = CreateDbContext())
        {
            var stored = await verify.Set<RoutineTimePeriod>().AsNoTracking().FirstAsync(p => p.Id == period.Id, CancellationToken);
            stored.EndingSoonNotifiedFor.Should().BeNull("a fully-done period must be left unmarked so an un-ticked item tomorrow still earns the nudge");
        }

        await using (var db = CreateDbContext())
        {
            var trackedItem = await db.Set<RoutineTodoList>().FirstAsync(i => i.Id == item.Id, CancellationToken);
            trackedItem.IsDone = false;
            await db.SaveChangesAsync(CancellationToken);
        }

        var secondNotifier = new RoutinePeriodNotificationServiceDouble();
        await RunJobWithDoubleAsync(secondNotifier, CancellationToken);

        secondNotifier.EndingSoonCalls.Select(c => c.PeriodId).Should().Contain(period.Id, "un-ticking the item must earn the nudge that the all-done sweep deliberately withheld");
    }

    // ---- D. Hidden periods ------------------------------------------------------------------------------

    [Fact]
    public async Task NudgeSweep_HiddenPeriod_SkipsBothNotifications()
    {
        await using (var db = CreateDbContext())
        {
            var period = await SeedPeriodAsync(db, UserId,
                reminderLeadDays: 3, lastResetAt: Now.AddDays(-5),
                isHidden: true, graceDays: 3, streakGraceUntil: Now.AddHours(12),
                ct: CancellationToken);
            await SeedItemAsync(db, period, isDone: false, UserId, CancellationToken);
        }

        var notifier = new RoutinePeriodNotificationServiceDouble();
        await RunJobWithDoubleAsync(notifier, CancellationToken);

        notifier.EndingSoonCalls.Should().BeEmpty();
        notifier.GraceCalls.Should().BeEmpty();
    }

    // ---- E. Boundary conditions -------------------------------------------------------------------------

    [Fact]
    public async Task NudgeSweep_NoLeadConfigured_NeverNudges()
    {
        await using (var db = CreateDbContext())
        {
            var period = await SeedPeriodAsync(db, UserId, lastResetAt: Now.AddDays(-5), ct: CancellationToken);
            await SeedItemAsync(db, period, isDone: false, UserId, CancellationToken);
        }

        var notifier = new RoutinePeriodNotificationServiceDouble();
        await RunJobWithDoubleAsync(notifier, CancellationToken);

        notifier.EndingSoonCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task NudgeSweep_DaysLeft_UsesCeilingNotFloor()
    {
        // ComputeNextReset always anchors to a UTC midnight (ResetAnchorDay == 0), so "the day after tomorrow's
        // midnight" is always strictly more than 1 and strictly less than 2 days from "now" regardless of the
        // current time of day -- landing reliably on the fractional day the ceiling behavior needs to prove out,
        // without the test being sensitive to what wall-clock time it happens to run at.
        var nextResetUtc = Now.Date.AddDays(2);
        var lastResetAt = nextResetUtc.AddDays(-7);
        long periodId;
        await using (var db = CreateDbContext())
        {
            var period = await SeedPeriodAsync(db, UserId, reminderLeadDays: 2, lastResetAt: lastResetAt, ct: CancellationToken);
            await SeedItemAsync(db, period, isDone: false, UserId, CancellationToken);
            periodId = period.Id;
        }

        var notifier = new RoutinePeriodNotificationServiceDouble();
        await RunJobWithDoubleAsync(notifier, CancellationToken);

        var call = notifier.EndingSoonCalls.Should().ContainSingle(c => c.PeriodId == periodId).Subject;
        call.DaysLeft.Should().Be(2, "a reset 1.2 days out must report 2 days left (ceiling), not 1 (floor)");
    }

    [Fact]
    public async Task NudgeSweep_GraceWarning_DayBefore_DoesNotFireYet()
    {
        await using (var db = CreateDbContext())
        {
            await SeedPeriodAsync(db, UserId, graceDays: 3, streakGraceUntil: Now.AddDays(2), ct: CancellationToken);
        }

        var notifier = new RoutinePeriodNotificationServiceDouble();
        await RunJobWithDoubleAsync(notifier, CancellationToken);

        notifier.GraceCalls.Should().BeEmpty("the grace warning has a 1-day lead, so 2 days out is still too early");
    }

    [Fact]
    public async Task NudgeSweep_GraceWarning_DayOf_Fires()
    {
        await using (var db = CreateDbContext())
        {
            await SeedPeriodAsync(db, UserId, graceDays: 3, streakGraceUntil: Now.AddHours(12), ct: CancellationToken);
        }

        var notifier = new RoutinePeriodNotificationServiceDouble();
        await RunJobWithDoubleAsync(notifier, CancellationToken);

        notifier.GraceCalls.Should().HaveCount(1);
    }

    [Fact]
    public async Task NudgeSweep_GraceWarning_AfterExpiry_DoesNotFire()
    {
        // Grace already lapsed. CheckGrace (in the reset job) owns breaking the streak for this case -- the
        // nudge sweep must not warn about a window that is already closed.
        await using (var db = CreateDbContext())
        {
            await SeedPeriodAsync(db, UserId, graceDays: 3, streakGraceUntil: Now.AddHours(-1), ct: CancellationToken);
        }

        var notifier = new RoutinePeriodNotificationServiceDouble();
        await RunJobWithDoubleAsync(notifier, CancellationToken);

        notifier.GraceCalls.Should().BeEmpty();
    }

    // ---- F. Cross-user scope ---------------------------------------------------------------------------

    [Fact]
    public async Task NudgeSweep_SweepsAllUsers_NotJustTheAmbientOne()
    {
        long periodAId, periodBId;
        await using (var db = CreateDbContext())
        {
            await ReminderSeedHelper.EnsureOtherUserAsync(db, CancellationToken);

            var periodA = await SeedPeriodAsync(db, UserId, reminderLeadDays: 3, lastResetAt: Now.AddDays(-5), ct: CancellationToken);
            await SeedItemAsync(db, periodA, isDone: false, UserId, CancellationToken);
            periodAId = periodA.Id;

            var periodB = await SeedPeriodAsync(db, ReminderSeedHelper.OtherUserId, reminderLeadDays: 3, lastResetAt: Now.AddDays(-5), ct: CancellationToken);
            await SeedItemAsync(db, periodB, isDone: false, ReminderSeedHelper.OtherUserId, CancellationToken);
            periodBId = periodB.Id;
        }

        var notifier = new RoutinePeriodNotificationServiceDouble();
        await RunJobWithDoubleAsync(notifier, CancellationToken);

        // Deliberate: the job runs unauthenticated, so the global IEntityWithUser query filter is a no-op and
        // every user's due periods are swept in one pass. Don't "fix" this to scope to one user.
        notifier.EndingSoonCalls.Select(c => c.PeriodId).Should().BeEquivalentTo([periodAId, periodBId]);
    }

    /// <summary>
    /// PERF-4. The sweep now walks the table a page of users at a time and commits the "notified for" markers
    /// per page, so paging is a mechanism that can silently drop users (a page that never gets its own save,
    /// or a <c>ChangeTracker.Clear()</c> that lands before one) without any error. Page size 1 puts each of the
    /// three users on their own page; the assertion is on the stored markers, not just the notify calls,
    /// because a lost per-page commit re-notifies everyone tomorrow while looking fine today.
    /// <para>
    /// The hidden period is here rather than in section D because the filter now appears twice — on the
    /// distinct-user-id query and on the page fetch — and a user whose only period is hidden must not be paged.
    /// </para>
    /// </summary>
    [Fact]
    public async Task NudgeSweep_AcrossPageBoundaries_NotifiesAndMarksEveryUser()
    {
        var thirdUserId = FakeLoggedUserService.TestUserId + 3;
        long periodAId, periodBId, periodCId, hiddenPeriodId;

        await using (var db = CreateDbContext())
        {
            await ReminderSeedHelper.EnsureOtherUserAsync(db, CancellationToken);
            await EnsureUserAsync(db, thirdUserId, null, CancellationToken);

            var periodA = await SeedPeriodAsync(db, UserId, reminderLeadDays: 3, lastResetAt: Now.AddDays(-5), ct: CancellationToken);
            await SeedItemAsync(db, periodA, isDone: false, UserId, CancellationToken);
            periodAId = periodA.Id;

            var periodB = await SeedPeriodAsync(db, ReminderSeedHelper.OtherUserId, reminderLeadDays: 3, lastResetAt: Now.AddDays(-5), ct: CancellationToken);
            await SeedItemAsync(db, periodB, isDone: false, ReminderSeedHelper.OtherUserId, CancellationToken);
            periodBId = periodB.Id;

            var periodC = await SeedPeriodAsync(db, thirdUserId, reminderLeadDays: 3, lastResetAt: Now.AddDays(-5), ct: CancellationToken);
            await SeedItemAsync(db, periodC, isDone: false, thirdUserId, CancellationToken);
            periodCId = periodC.Id;

            // Same user as period C, on a different length so the unique (user_id, length_in_days) index holds:
            // one page must still be able to hold a mix.
            var hidden = await SeedPeriodAsync(db, thirdUserId, lengthInDays: 14, reminderLeadDays: 3,
                lastResetAt: Now.AddDays(-12), isHidden: true, ct: CancellationToken);
            await SeedItemAsync(db, hidden, isDone: false, thirdUserId, CancellationToken);
            hiddenPeriodId = hidden.Id;
        }

        var notifier = new RoutinePeriodNotificationServiceDouble();
        await RunJobWithDoubleAsync(notifier, CancellationToken, userPageSize: 1);

        notifier.EndingSoonCalls.Select(c => c.PeriodId).Should()
            .BeEquivalentTo([periodAId, periodBId, periodCId], "every page must be swept, and only the hidden period skipped");

        await using (var verify = CreateDbContext())
        {
            var stored = await verify.Set<RoutineTimePeriod>().AsNoTracking()
                .Where(p => p.Id == periodAId || p.Id == periodBId || p.Id == periodCId || p.Id == hiddenPeriodId)
                .ToDictionaryAsync(p => p.Id, CancellationToken);

            stored[periodAId].EndingSoonNotifiedFor.Should().NotBeNull("the first page's marker must be committed by its own save");
            stored[periodBId].EndingSoonNotifiedFor.Should().NotBeNull();
            stored[periodCId].EndingSoonNotifiedFor.Should().NotBeNull("the last page must be committed too");
            stored[hiddenPeriodId].EndingSoonNotifiedFor.Should().BeNull("a hidden period is never swept, so it is never marked");
        }
    }


    // ---- G. Delivery waits for the user's own morning (P1-01 B) ------------------------------------------

    /// <summary>The weekly reset every case below is about: Thursday 1 October 2020, 00:00 UTC.</summary>
    private static readonly DateTime LastResetBeforeThursday = new(2020, 9, 24, 0, 0, 0, DateTimeKind.Utc);

    private static DateTime Sep30(int hour, int minute = 0) => new(2020, 9, 30, hour, minute, 0, DateTimeKind.Utc);

    private static async Task SeedWakeUpAsync(DbContext db, long userId, DateOnly date, TimeOnly wakeUp, CancellationToken ct)
    {
        db.Set<Calendar>().Add(new Calendar
        {
            UserId = userId,
            Date = date,
            DayType = AdhdTimeOrganizer.Core.domain.model.@enum.DayType.Workday,
            WakeUpTime = wakeUp,
            BedTime = new TimeOnly(23, 0)
        });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// The defect: one 09:00 UTC run for everyone. Three users with the same UTC-anchored weekly period and a
    /// one-day lead, so one shared window — [30 Sep 00:00, 1 Oct 00:00) UTC — and no calendar rows, so each
    /// morning is the default 08:00 wake-up + 1 h in the user's own zone: Tokyo 00:00 UTC, Bratislava (CEST)
    /// 07:00 UTC, New York (EDT) 13:00 UTC. The sweep runs at the quarter hours either side of each, and each
    /// user is nudged at their own run and at no other — the last run, after all three, sends nothing again.
    /// Asserted on the persisted marks too, since a mark that never lands re-sends on every one of 96 runs.
    /// </summary>
    [Fact(DisplayName = "P1-01 B: each user's nudge goes out at their own wake-up + 1 h, once")]
    public async Task NudgeSweep_DeliversAtEachUsersOwnMorning_AndOnlyOnce()
    {
        var tokyoUserId = FakeLoggedUserService.TestUserId + 5;
        var bratislavaUserId = FakeLoggedUserService.TestUserId + 6;
        var newYorkUserId = FakeLoggedUserService.TestUserId + 7;
        var periodIds = new Dictionary<long, long>();

        await using (var db = CreateDbContext())
        {
            foreach (var (userId, zone) in new[] { (tokyoUserId, Tokyo), (bratislavaUserId, Bratislava), (newYorkUserId, NewYork) })
            {
                await EnsureUserAsync(db, userId, zone, CancellationToken);
                var period = await SeedPeriodAsync(db, userId, reminderLeadDays: 1, lastResetAt: LastResetBeforeThursday, ct: CancellationToken);
                await SeedItemAsync(db, period, isDone: false, userId, CancellationToken);
                periodIds[userId] = period.Id;
            }
        }

        var notifier = new RoutinePeriodNotificationServiceDouble();
        var sentAt = new List<string>();
        foreach (var run in new[]
                 {
                     new DateTime(2020, 9, 29, 23, 45, 0, DateTimeKind.Utc), Sep30(0), Sep30(6, 45), Sep30(7),
                     Sep30(12, 45), Sep30(13), Sep30(13, 15)
                 })
        {
            var before = notifier.EndingSoonCalls.Count;
            await RunJobWithDoubleAsync(notifier, CancellationToken, at: run);
            sentAt.Add($"{run:dd HH:mm} -> [{string.Join(",", notifier.EndingSoonCalls.Skip(before).Select(c => c.UserId))}]");
        }

        sentAt.Should().Equal(
            [
                "29 23:45 -> []",
                $"30 00:00 -> [{tokyoUserId}]",
                "30 06:45 -> []",
                $"30 07:00 -> [{bratislavaUserId}]",
                "30 12:45 -> []",
                $"30 13:00 -> [{newYorkUserId}]",
                "30 13:15 -> []"
            ],
            "the window is UTC and shared, the morning is each user's own — and a mark stops every later run");

        await using var verify = CreateDbContext();
        var marks = await verify.Set<RoutineTimePeriod>().IgnoreQueryFilters().AsNoTracking()
            .Where(p => periodIds.Values.Contains(p.Id))
            .Select(p => p.EndingSoonNotifiedFor)
            .ToListAsync(CancellationToken);
        marks.Should().HaveCount(3).And.AllBeEquivalentTo(new DateTime(2020, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            "the mark is still the UTC reset instant — only when it was written moved");
    }

    /// <summary>
    /// The morning is the day's own calendar wake-up when the row exists, not the 08:00 default — the same
    /// resolution the suggested-today digest uses.
    /// </summary>
    [Fact(DisplayName = "P1-01 B: the day's calendar wake-up moves the delivery moment")]
    public async Task NudgeSweep_UsesTheDaysCalendarWakeUp()
    {
        await using (var db = CreateDbContext())
        {
            var period = await SeedPeriodAsync(db, UserId, reminderLeadDays: 1, lastResetAt: LastResetBeforeThursday, ct: CancellationToken);
            await SeedItemAsync(db, period, isDone: false, UserId, CancellationToken);
            await SeedWakeUpAsync(db, UserId, new DateOnly(2020, 9, 30), new TimeOnly(5, 30), CancellationToken);
        }

        var notifier = new RoutinePeriodNotificationServiceDouble();
        await RunJobWithDoubleAsync(notifier, CancellationToken, at: Sep30(6, 15));
        notifier.EndingSoonCalls.Should().BeEmpty("a 05:30 wake-up puts the nudge at 06:30");

        await RunJobWithDoubleAsync(notifier, CancellationToken, at: Sep30(6, 30));
        notifier.EndingSoonCalls.Should().ContainSingle().Which.UserId.Should().Be(UserId);
    }

    /// <summary>
    /// The grace warning is gated the same way. New York's grace window [29 Sep 00:00, 30 Sep 00:00) UTC opens
    /// at 20:00 the evening before, local — past that day's morning — so it waits for the next one, 13:00 UTC.
    /// </summary>
    [Fact(DisplayName = "P1-01 B: the grace warning waits for the user's morning too")]
    public async Task NudgeSweep_GraceWarning_WaitsForTheUsersMorning()
    {
        var newYorkUserId = FakeLoggedUserService.TestUserId + 7;
        long periodId;
        await using (var db = CreateDbContext())
        {
            await EnsureUserAsync(db, newYorkUserId, NewYork, CancellationToken);
            var period = await SeedPeriodAsync(db, newYorkUserId, graceDays: 3,
                streakGraceUntil: new DateTime(2020, 9, 30, 0, 0, 0, DateTimeKind.Utc), ct: CancellationToken);
            periodId = period.Id;
        }

        var notifier = new RoutinePeriodNotificationServiceDouble();
        await RunJobWithDoubleAsync(notifier, CancellationToken, at: new DateTime(2020, 9, 29, 0, 15, 0, DateTimeKind.Utc));
        await RunJobWithDoubleAsync(notifier, CancellationToken, at: new DateTime(2020, 9, 29, 12, 45, 0, DateTimeKind.Utc));
        notifier.GraceCalls.Should().BeEmpty("the old 09:00 UTC run would have woken New York at 05:00");

        await RunJobWithDoubleAsync(notifier, CancellationToken, at: new DateTime(2020, 9, 29, 13, 0, 0, DateTimeKind.Utc));
        notifier.GraceCalls.Select(c => c.PeriodId).Should().Equal(periodId);

        await using var verify = CreateDbContext();
        (await verify.Set<RoutineTimePeriod>().IgnoreQueryFilters().AsNoTracking().SingleAsync(p => p.Id == periodId, CancellationToken))
            .GraceNotifiedFor.Should().Be(new DateTime(2020, 9, 30, 0, 0, 0, DateTimeKind.Utc));
    }

    /// <summary>
    /// The safety branch: a window with no morning in it. A 23:30 wake-up puts the only moment of 30 September
    /// at 00:30 on 1 October, after the reset — so instead of never, the nudge goes as soon as the window opens.
    /// For the grace warning that is the difference between an odd hour and a streak broken without a word.
    /// </summary>
    [Fact(DisplayName = "P1-01 B: a window with no morning inside it is delivered as soon as it opens")]
    public async Task NudgeSweep_WindowWithNoMorningInside_DeliversAtOpening()
    {
        await using (var db = CreateDbContext())
        {
            var period = await SeedPeriodAsync(db, UserId, reminderLeadDays: 1, lastResetAt: LastResetBeforeThursday, ct: CancellationToken);
            await SeedItemAsync(db, period, isDone: false, UserId, CancellationToken);
            await SeedWakeUpAsync(db, UserId, new DateOnly(2020, 9, 30), new TimeOnly(23, 30), CancellationToken);
        }

        var notifier = new RoutinePeriodNotificationServiceDouble();
        await RunJobWithDoubleAsync(notifier, CancellationToken, at: Sep30(0, 15));

        notifier.EndingSoonCalls.Should().ContainSingle().Which.UserId.Should().Be(UserId);
    }
}
