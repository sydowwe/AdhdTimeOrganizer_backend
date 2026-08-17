using System.Security.Claims;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.IntegrationTests.Reminders;
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
/// </summary>
[Collection("Postgres")]
public class RoutinePeriodNudgeJobTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    // ---- test double -------------------------------------------------------------------------------

    private sealed class RoutinePeriodNotificationServiceDouble : IRoutinePeriodNotificationService
    {
        public sealed record EndingSoonCall(long PeriodId, long UserId, int Remaining, int Total, int DaysLeft);
        public sealed record GraceCall(long PeriodId, long UserId);

        public List<EndingSoonCall> EndingSoonCalls { get; } = [];
        public List<GraceCall> GraceCalls { get; } = [];

        /// <summary>1-based ordinal across both notify methods, in call order. 0 = never throw.</summary>
        public int ThrowOnCallNumber { get; set; }

        private int _callCount;

        public Task NotifyPeriodEndedAsync(RoutineTimePeriod period, RoutinePeriodCompletion completion, StreakOutcome outcome, CancellationToken ct = default)
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

        public Task NotifyGraceExpiringAsync(RoutineTimePeriod period, CancellationToken ct = default)
        {
            _callCount++;
            GraceCalls.Add(new GraceCall(period.Id, period.UserId));
            if (_callCount == ThrowOnCallNumber)
                throw new InvalidOperationException($"stub notifier failure on call {_callCount}");

            return Task.CompletedTask;
        }
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
    private static async Task RunJobAsync(IServiceProvider services, CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var handler = (IScheduledJobHandler)ActivatorUtilities.CreateInstance(scope.ServiceProvider, typeof(RoutinePeriodNudgeJobHandler));

        var now = DateTime.UtcNow;
        await handler.ExecuteAsync(new ScheduledJobContext
        {
            ScheduledFireTimeUtc = now,
            ActualFireTimeUtc = now,
            JobKey = handler.Key,
            CorrelationId = "routine-nudge-job-test",
            TriggerSource = TriggerSource.Manual
        }, ct);
    }

    private async Task RunJobWithDoubleAsync(RoutinePeriodNotificationServiceDouble notifier, CancellationToken ct)
    {
        await using var factory = CreateFactory(TestRoles.AdminAndUser, configureServices: services =>
        {
            services.RemoveAll<IRoutinePeriodNotificationService>();
            services.AddSingleton<IRoutinePeriodNotificationService>(notifier);

            // See NoAmbientUserLoggedUserService: without this override every query through this factory's
            // DbContext is silently scoped to the factory's default userId, which is not how the real
            // unauthenticated job scope behaves.
            services.RemoveAll<ILoggedUserService>();
            services.AddScoped<ILoggedUserService>(_ => new NoAmbientUserLoggedUserService());
        });
        await RunJobAsync(factory.Services, ct);
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

            var period1 = await SeedPeriodAsync(db, UserId, reminderLeadDays: 3, lastResetAt: DateTime.UtcNow.AddDays(-5), ct: CancellationToken);
            await SeedItemAsync(db, period1, isDone: false, UserId, CancellationToken);
            period1Id = period1.Id;

            var period2 = await SeedPeriodAsync(db, ReminderSeedHelper.OtherUserId, reminderLeadDays: 3, lastResetAt: DateTime.UtcNow.AddDays(-5), ct: CancellationToken);
            await SeedItemAsync(db, period2, isDone: false, ReminderSeedHelper.OtherUserId, CancellationToken);
            period2Id = period2.Id;

            var period3 = await SeedPeriodAsync(db, thirdUserId, reminderLeadDays: 3, lastResetAt: DateTime.UtcNow.AddDays(-5), ct: CancellationToken);
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

    // ---- B. Idempotent marking ------------------------------------------------------------------------

    [Fact]
    public async Task NudgeSweep_LeadTimeNudge_IsSentExactlyOnceAcrossTwoRuns()
    {
        await using (var db = CreateDbContext())
        {
            var period = await SeedPeriodAsync(db, UserId, reminderLeadDays: 3, lastResetAt: DateTime.UtcNow.AddDays(-5), ct: CancellationToken);
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
            await SeedPeriodAsync(db, UserId, graceDays: 3, streakGraceUntil: DateTime.UtcNow.AddHours(12), ct: CancellationToken);
        }

        var notifier = new RoutinePeriodNotificationServiceDouble();
        await RunJobWithDoubleAsync(notifier, CancellationToken);
        await RunJobWithDoubleAsync(notifier, CancellationToken);

        notifier.GraceCalls.Should().HaveCount(1);
    }

    // ---- C. Fully-done periods are skipped without marking ---------------------------------------------

    [Fact]
    public async Task NudgeSweep_AllItemsDone_SkipsWithoutMarking_ThenNudgesOnceAnItemIsUnticked()
    {
        RoutineTimePeriod period;
        RoutineTodoList item;
        await using (var db = CreateDbContext())
        {
            period = await SeedPeriodAsync(db, UserId, reminderLeadDays: 3, lastResetAt: DateTime.UtcNow.AddDays(-5), ct: CancellationToken);
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
                reminderLeadDays: 3, lastResetAt: DateTime.UtcNow.AddDays(-5),
                isHidden: true, graceDays: 3, streakGraceUntil: DateTime.UtcNow.AddHours(12),
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
            var period = await SeedPeriodAsync(db, UserId, lastResetAt: DateTime.UtcNow.AddDays(-5), ct: CancellationToken);
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
        var nextResetUtc = DateTime.UtcNow.Date.AddDays(2);
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
            await SeedPeriodAsync(db, UserId, graceDays: 3, streakGraceUntil: DateTime.UtcNow.AddDays(2), ct: CancellationToken);
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
            await SeedPeriodAsync(db, UserId, graceDays: 3, streakGraceUntil: DateTime.UtcNow.AddHours(12), ct: CancellationToken);
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
            await SeedPeriodAsync(db, UserId, graceDays: 3, streakGraceUntil: DateTime.UtcNow.AddHours(-1), ct: CancellationToken);
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

            var periodA = await SeedPeriodAsync(db, UserId, reminderLeadDays: 3, lastResetAt: DateTime.UtcNow.AddDays(-5), ct: CancellationToken);
            await SeedItemAsync(db, periodA, isDone: false, UserId, CancellationToken);
            periodAId = periodA.Id;

            var periodB = await SeedPeriodAsync(db, ReminderSeedHelper.OtherUserId, reminderLeadDays: 3, lastResetAt: DateTime.UtcNow.AddDays(-5), ct: CancellationToken);
            await SeedItemAsync(db, periodB, isDone: false, ReminderSeedHelper.OtherUserId, CancellationToken);
            periodBId = periodB.Id;
        }

        var notifier = new RoutinePeriodNotificationServiceDouble();
        await RunJobWithDoubleAsync(notifier, CancellationToken);

        // Deliberate: the job runs unauthenticated, so the global IEntityWithUser query filter is a no-op and
        // every user's due periods are swept in one pass. Don't "fix" this to scope to one user.
        notifier.EndingSoonCalls.Select(c => c.PeriodId).Should().BeEquivalentTo([periodAId, periodBId]);
    }

    // ---- G. Timezone (exploratory) ---------------------------------------------------------------------

    /// <summary>
    /// TEST-13 Section G. <c>RoutinePeriodNudgeJobHandler</c> captures <c>now</c> once as
    /// <c>DateTime.UtcNow</c> and applies it to every period regardless of <c>User.Timezone</c> — the job never
    /// even loads the <c>User</c> row. This pins the CURRENT behavior (two users a full day apart in UTC offset
    /// get an identical lead-time nudge for identically UTC-anchored periods) rather than asserting it is
    /// correct: a job documented as a "09:00 wall-clock sweep" plausibly means each user's own 09:00, and a
    /// reset anchored to UTC midnight is nobody's local midnight in particular. Forcing an actually-diverging
    /// case deterministically would need a controllable clock the job doesn't have (it reads
    /// <c>DateTime.UtcNow</c> directly, not an injected time provider), so this is recorded as a finding via
    /// this comment rather than as a flaky boundary-crossing assertion. Not tagged <c>KnownGap</c> — that trait
    /// is reserved for CQ-5; there is no fix reference for this one yet.
    /// </summary>
    [Fact]
    public async Task NudgeSweep_IgnoresUserTimezone_IdenticalUtcPeriodsProduceIdenticalResultsRegardlessOfOffset()
    {
        var nextResetUtc = DateTime.UtcNow.Date.AddDays(1);
        var lastResetAt = nextResetUtc.AddDays(-7);

        var aheadTz = TimeZoneInfo.CreateCustomTimeZone("Test/UTC+14", TimeSpan.FromHours(14), "Test UTC+14", "Test UTC+14");
        var behindTz = TimeZoneInfo.CreateCustomTimeZone("Test/UTC-11", TimeSpan.FromHours(-11), "Test UTC-11", "Test UTC-11");
        var aheadUserId = FakeLoggedUserService.TestUserId + 5;
        var behindUserId = FakeLoggedUserService.TestUserId + 6;

        long aheadPeriodId, behindPeriodId;
        await using (var db = CreateDbContext())
        {
            await EnsureUserAsync(db, aheadUserId, aheadTz, CancellationToken);
            await EnsureUserAsync(db, behindUserId, behindTz, CancellationToken);

            var aheadPeriod = await SeedPeriodAsync(db, aheadUserId, reminderLeadDays: 1, lastResetAt: lastResetAt, ct: CancellationToken);
            await SeedItemAsync(db, aheadPeriod, isDone: false, aheadUserId, CancellationToken);
            aheadPeriodId = aheadPeriod.Id;

            var behindPeriod = await SeedPeriodAsync(db, behindUserId, reminderLeadDays: 1, lastResetAt: lastResetAt, ct: CancellationToken);
            await SeedItemAsync(db, behindPeriod, isDone: false, behindUserId, CancellationToken);
            behindPeriodId = behindPeriod.Id;
        }

        var notifier = new RoutinePeriodNotificationServiceDouble();
        await RunJobWithDoubleAsync(notifier, CancellationToken);

        var aheadCall = notifier.EndingSoonCalls.SingleOrDefault(c => c.PeriodId == aheadPeriodId);
        var behindCall = notifier.EndingSoonCalls.SingleOrDefault(c => c.PeriodId == behindPeriodId);

        aheadCall.Should().NotBeNull();
        behindCall.Should().NotBeNull();
        aheadCall!.DaysLeft.Should().Be(behindCall!.DaysLeft,
            "current behavior: both periods are evaluated against one shared UTC clock with no reference to " +
            "User.Timezone at all, so a 25-hour offset gap between the two users changes nothing about the " +
            "result -- pinned here so a future timezone-aware rewrite changes this test deliberately, not by accident");
    }
}
