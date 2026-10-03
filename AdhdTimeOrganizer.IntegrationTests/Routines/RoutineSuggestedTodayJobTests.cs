using System.Security.Claims;
using System.Text.Json;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.Core.domain.model.@enum;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
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
using Sydowwe.Framework.Contracts.notification;
using Sydowwe.Framework.Contracts.scheduling;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.domain.extServiceContract.user;
using Sydowwe.Framework.Testing;
using Sydowwe.Notifications.application;
using Sydowwe.Notifications.domain.entity;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Routines;

/// <summary>
/// Backend ask B4 — <c>RoutineSuggestedTodayJobHandler</c>, the morning digest of the routine items suggested
/// for the user's today and not done yet.
/// <para>
/// Every case pins the clock: which items qualify depends on the weekday and the day of the month <i>in the
/// user's zone</i>, and whether the digest may go out depends on the hour there. The anchor is Tuesday
/// 29 September 2020, the last-but-one day of a 30-day month.
/// </para>
/// <para>
/// <b>Why a date years in the past.</b> Quartz runs inside every test host, and this job fires every quarter
/// hour against the shared database with the <i>real</i> clock. Pinned to the real today, such a fire lands
/// mid-test, sends the real digest and stamps the very date the test is about — the case then sees one call
/// too few. A pinned date no real clock will ever produce keeps those fires out of every assertion here.
/// </para>
/// <para>
/// Most cases substitute the notifier with a double
/// so the assertion is on exactly what the job handed it; the last group runs the real pipeline and asserts on
/// the persisted notification row and what the renderer makes of it.
/// </para>
/// </summary>
[Collection("Postgres")]
public class RoutineSuggestedTodayJobTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    /// <summary>Tuesday 29 September 2020, 10:00 UTC — past the default 08:00 morning for a UTC user.</summary>
    private static readonly DateTimeOffset TuesdayMorning = new(2020, 9, 29, 10, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly Tuesday = new(2020, 9, 29);

    // ---- test doubles -----------------------------------------------------------------------------

    private sealed class NotifierDouble : IRoutinePeriodNotificationService
    {
        public sealed record DigestCall(long UserId, DateOnly Date, IReadOnlyList<long> ItemIds);

        public List<DigestCall> Calls { get; } = [];

        /// <summary>Mirrors the real implementation swallowing a dispatch failure: recorded, and reported as not sent.</summary>
        public bool DispatchFails { get; set; }

        public Task NotifyPeriodEndedAsync(RoutineTimePeriod period, RoutinePeriodCompletion completion, StreakOutcome outcome, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<bool> NotifyEndingSoonAsync(RoutineTimePeriod period, RoutineResetService.RoutineNudge nudge, int remaining, int total, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<bool> NotifyGraceExpiringAsync(RoutineTimePeriod period, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<bool> NotifySuggestedTodayAsync(long userId, DateOnly date, IReadOnlyList<RoutineTodoList> items, CancellationToken ct = default)
        {
            Calls.Add(new DigestCall(userId, date, items.Select(i => i.Id).ToList()));
            return Task.FromResult(!DispatchFails);
        }
    }

    /// <summary>
    /// The real background scope has no user, which is what makes the sweep see everyone. See the copy in
    /// <c>RoutinePeriodNudgeJobTests</c> for why <see cref="FakeLoggedUserService"/> cannot stand in.
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

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    // ---- helpers ----------------------------------------------------------------------------------

    private static async Task<RoutineTimePeriod> SeedPeriodAsync(
        DbContext db, long userId, int lengthInDays, bool isHidden = false, DateTime? lastResetAt = null, CancellationToken ct = default)
    {
        var period = new RoutineTimePeriod
        {
            UserId = userId,
            // Unique per test: (user_id, text) and (user_id, length_in_days) are both unique.
            Text = $"Period {Guid.NewGuid():N}",
            Color = "#123456",
            LengthInDays = lengthInDays,
            ResetAnchorDay = 0,
            StreakThreshold = 100,
            StreakGraceDays = 0,
            IsHidden = isHidden,
            // Recent enough that no reset is due at the pinned clock unless a test says otherwise.
            LastResetAt = lastResetAt ?? TuesdayMorning.UtcDateTime.AddDays(-1)
        };
        db.Set<RoutineTimePeriod>().Add(period);
        await db.SaveChangesAsync(ct);
        return period;
    }

    private static async Task<RoutineTodoList> SeedItemAsync(
        DbContext db,
        RoutineTimePeriod period,
        string name,
        DayOfWeek[]? days = null,
        int? dayOfMonth = null,
        bool isDone = false,
        CancellationToken ct = default)
    {
        var role = new ActivityRole { UserId = period.UserId, Name = $"Role {Guid.NewGuid():N}", Color = "#123456" };
        db.Set<ActivityRole>().Add(role);
        await db.SaveChangesAsync(ct);

        var activity = new Activity { UserId = period.UserId, Name = $"{name} {Guid.NewGuid():N}", RoleId = role.Id };
        db.Set<Activity>().Add(activity);
        await db.SaveChangesAsync(ct);

        var item = new RoutineTodoList
        {
            UserId = period.UserId,
            ActivityId = activity.Id,
            TimePeriodId = period.Id,
            IsDone = isDone,
            SuggestedDays = [.. days ?? []],
            SuggestedDayOfMonth = dayOfMonth
        };
        db.Set<RoutineTodoList>().Add(item);
        await db.SaveChangesAsync(ct);
        return item;
    }

    private static async Task EnsureUserAsync(DbContext db, long userId, TimeZoneInfo timezone, CancellationToken ct)
    {
        if (await db.Set<User>().FindAsync([userId], ct) is { } existing)
        {
            existing.Timezone = timezone;
            await db.SaveChangesAsync(ct);
            return;
        }

        var email = $"suggested-{userId}@test.com";
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
            Timezone = timezone
        };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, AppDbContextFixture.TestUserPassword);
        db.Set<User>().Add(user);
        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedCalendarDayAsync(DbContext db, long userId, DateOnly date, TimeOnly wakeUp, CancellationToken ct)
    {
        db.Set<Calendar>().Add(new Calendar
        {
            UserId = userId,
            Date = date,
            DayType = DayType.Workday,
            WakeUpTime = wakeUp,
            BedTime = new TimeOnly(23, 0)
        });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Runs the job the way the Scheduler dispatcher does — one scope per fire, no ambient user — with the clock
    /// pinned at <paramref name="now"/>, and the notifier substituted when a double is given.
    /// </summary>
    private async Task RunJobAsync(DateTimeOffset now, NotifierDouble? notifier = null, int? userPageSize = null)
    {
        await using var factory = CreateFactory(TestRoles.AdminAndUser, configureServices: services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new FixedTimeProvider(now));

            services.RemoveAll<ILoggedUserService>();
            services.AddScoped<ILoggedUserService>(_ => new NoAmbientUserLoggedUserService());

            if (notifier is not null)
            {
                services.RemoveAll<IRoutinePeriodNotificationService>();
                services.AddSingleton<IRoutinePeriodNotificationService>(notifier);
            }
        });

        await using var scope = factory.Services.CreateAsyncScope();
        var handler = (IScheduledJobHandler)(userPageSize is { } size
            ? ActivatorUtilities.CreateInstance(scope.ServiceProvider, typeof(RoutineSuggestedTodayJobHandler), size)
            : ActivatorUtilities.CreateInstance(scope.ServiceProvider, typeof(RoutineSuggestedTodayJobHandler)));

        await handler.ExecuteAsync(new ScheduledJobContext
        {
            ScheduledFireTimeUtc = now.UtcDateTime,
            ActualFireTimeUtc = now.UtcDateTime,
            JobKey = handler.Key,
            CorrelationId = "routine-suggested-today-test",
            TriggerSource = TriggerSource.Manual
        }, CancellationToken);
    }

    private async Task<Dictionary<long, DateOnly?>> ReadMarksAsync(long userId)
    {
        await using var db = CreateDbContext();
        return await db.Set<RoutineTimePeriod>().IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.UserId == userId)
            .ToDictionaryAsync(p => p.Id, p => p.SuggestedTodayNotifiedFor, CancellationToken);
    }

    // ---- the suggested-day rule ------------------------------------------------------------------

    /// <summary>
    /// Rule 2, the half the client also ships in <c>routineSuggestedDay.ts</c>. Pure, so it is pinned directly:
    /// the month-end clamp is the case a job-level test would need a different clock for every month to see.
    /// </summary>
    [Theory(DisplayName = "B4: a monthly day is clamped to the month's last day, and a month is never skipped")]
    [InlineData(31, 2026, 9, 30, true)]   // "the 31st" is 30 September
    [InlineData(31, 2026, 9, 29, false)]
    [InlineData(30, 2026, 2, 28, true)]   // "the 30th" is 28 February…
    [InlineData(30, 2028, 2, 29, true)]   // …and 29 February in a leap year
    [InlineData(30, 2028, 2, 28, false)]
    [InlineData(15, 2026, 9, 15, true)]
    [InlineData(15, 2026, 9, 16, false)]
    public void MonthlyItem_IsSuggestedOnItsClampedDay(int dayOfMonth, int year, int month, int day, bool expected)
    {
        var period = new RoutineTimePeriod { Text = "m", Color = "#000000", LengthInDays = 30, ResetAnchorDay = 0, StreakThreshold = 100, StreakGraceDays = 0 };
        var item = new RoutineTodoList { TimePeriodId = 0, SuggestedDayOfMonth = dayOfMonth };

        RoutineSuggestedDay.IsSuggestedOn(period, item, new DateOnly(year, month, day)).Should().Be(expected);
    }

    [Fact(DisplayName = "B4: a daily period's item is never suggested, whatever its fields say")]
    public void DailyItem_IsNeverSuggested()
    {
        var period = new RoutineTimePeriod { Text = "d", Color = "#000000", LengthInDays = 1, ResetAnchorDay = 0, StreakThreshold = 100, StreakGraceDays = 0 };
        var item = new RoutineTodoList { TimePeriodId = 0, SuggestedDays = [.. Enum.GetValues<DayOfWeek>()], SuggestedDayOfMonth = Tuesday.Day };

        RoutineSuggestedDay.IsSuggestedOn(period, item, Tuesday).Should().BeFalse();
    }

    // ---- what qualifies --------------------------------------------------------------------------

    /// <summary>
    /// The client's verification step 1 and 7 in one: today's weekly item is named, another weekday's is not, and
    /// a daily item carrying all seven weekdays (as the e2e seed's do) never appears. One call, not one per item.
    /// </summary>
    [Fact(DisplayName = "B4: one digest naming only today's open items; daily routines never appear")]
    public async Task Digest_NamesOnlyTodaysOpenNonDailyItems()
    {
        long todayItemId, weeklyId, dailyId;
        await using (var db = CreateDbContext())
        {
            var weekly = await SeedPeriodAsync(db, UserId, lengthInDays: 7, ct: CancellationToken);
            var daily = await SeedPeriodAsync(db, UserId, lengthInDays: 1, ct: CancellationToken);
            weeklyId = weekly.Id;
            dailyId = daily.Id;

            todayItemId = (await SeedItemAsync(db, weekly, "Gym", days: [DayOfWeek.Tuesday, DayOfWeek.Thursday], ct: CancellationToken)).Id;
            await SeedItemAsync(db, weekly, "Laundry", days: [DayOfWeek.Saturday], ct: CancellationToken);
            await SeedItemAsync(db, weekly, "Unscheduled", ct: CancellationToken);
            await SeedItemAsync(db, daily, "Brush teeth", days: [.. Enum.GetValues<DayOfWeek>()], ct: CancellationToken);
        }

        var notifier = new NotifierDouble();
        await RunJobAsync(TuesdayMorning, notifier);

        notifier.Calls.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new NotifierDouble.DigestCall(UserId, Tuesday, [todayItemId]));

        var marks = await ReadMarksAsync(UserId);
        marks[weeklyId].Should().Be(Tuesday);
        marks[dailyId].Should().Be(Tuesday, "the mark is per user, so every one of their periods carries it");
    }

    [Fact(DisplayName = "B4: a second run the same day sends nothing new")]
    public async Task Digest_IsSentOncePerDay()
    {
        await using (var db = CreateDbContext())
        {
            var weekly = await SeedPeriodAsync(db, UserId, lengthInDays: 7, ct: CancellationToken);
            await SeedItemAsync(db, weekly, "Gym", days: [DayOfWeek.Tuesday], ct: CancellationToken);
        }

        var notifier = new NotifierDouble();
        await RunJobAsync(TuesdayMorning, notifier);
        await RunJobAsync(TuesdayMorning.AddMinutes(15), notifier);

        // A period created after the digest went out does not reopen the day either.
        await using (var db = CreateDbContext())
        {
            var monthly = await SeedPeriodAsync(db, UserId, lengthInDays: 30, ct: CancellationToken);
            await SeedItemAsync(db, monthly, "Pay rent", dayOfMonth: Tuesday.Day, ct: CancellationToken);
        }
        await RunJobAsync(TuesdayMorning.AddMinutes(30), notifier);

        notifier.Calls.Should().ContainSingle();
    }

    /// <summary>
    /// Rules 1 and 5 — and the reason a skipped day is left unmarked: an item un-ticked later that morning
    /// still earns the digest.
    /// </summary>
    [Fact(DisplayName = "B4: a done item is not named; with nothing left, nothing is sent and the day stays open")]
    public async Task DoneItem_SendsNothing_AndLeavesTheDayUnmarked()
    {
        long weeklyId, itemId;
        await using (var db = CreateDbContext())
        {
            var weekly = await SeedPeriodAsync(db, UserId, lengthInDays: 7, ct: CancellationToken);
            weeklyId = weekly.Id;
            itemId = (await SeedItemAsync(db, weekly, "Gym", days: [DayOfWeek.Tuesday], isDone: true, ct: CancellationToken)).Id;
        }

        var notifier = new NotifierDouble();
        await RunJobAsync(TuesdayMorning, notifier);

        notifier.Calls.Should().BeEmpty();
        (await ReadMarksAsync(UserId))[weeklyId].Should().BeNull();

        await using (var db = CreateDbContext())
        {
            var item = await db.Set<RoutineTodoList>().SingleAsync(i => i.Id == itemId, CancellationToken);
            item.IsDone = false;
            await db.SaveChangesAsync(CancellationToken);
        }

        await RunJobAsync(TuesdayMorning.AddMinutes(15), notifier);

        notifier.Calls.Should().ContainSingle().Which.ItemIds.Should().Equal(itemId);
    }

    /// <summary>
    /// A tick from a cycle whose reset is already due but not yet applied (the reset job runs at 02:00 UTC) is
    /// last cycle's, and must not hide today's item.
    /// </summary>
    [Fact(DisplayName = "B4: a tick left over from a cycle whose reset is due does not count as done")]
    public async Task StaleTickFromAClosedCycle_DoesNotCountAsDone()
    {
        long itemId;
        await using (var db = CreateDbContext())
        {
            var weekly = await SeedPeriodAsync(db, UserId, lengthInDays: 7, lastResetAt: TuesdayMorning.UtcDateTime.AddDays(-8), ct: CancellationToken);
            itemId = (await SeedItemAsync(db, weekly, "Gym", days: [DayOfWeek.Tuesday], isDone: true, ct: CancellationToken)).Id;
        }

        var notifier = new NotifierDouble();
        await RunJobAsync(TuesdayMorning, notifier);

        notifier.Calls.Should().ContainSingle().Which.ItemIds.Should().Equal(itemId);
    }

    [Fact(DisplayName = "B4: a monthly item for the 31st is named on the last day of a 30-day month")]
    public async Task MonthlyItemForThe31st_IsNamedOnThe30th()
    {
        long itemId;
        await using (var db = CreateDbContext())
        {
            var monthly = await SeedPeriodAsync(db, UserId, lengthInDays: 30, ct: CancellationToken);
            itemId = (await SeedItemAsync(db, monthly, "Pay rent", dayOfMonth: 31, ct: CancellationToken)).Id;
        }

        var notifier = new NotifierDouble();
        await RunJobAsync(TuesdayMorning, notifier);
        notifier.Calls.Should().BeEmpty("29 September is not the month's last day");

        await RunJobAsync(TuesdayMorning.AddDays(1), notifier);
        notifier.Calls.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new NotifierDouble.DigestCall(UserId, Tuesday.AddDays(1), [itemId]));
    }

    [Fact(DisplayName = "B4: a hidden period's items are never named")]
    public async Task HiddenPeriod_IsSkipped()
    {
        await using (var db = CreateDbContext())
        {
            var hidden = await SeedPeriodAsync(db, UserId, lengthInDays: 7, isHidden: true, ct: CancellationToken);
            await SeedItemAsync(db, hidden, "Gym", days: [DayOfWeek.Tuesday], ct: CancellationToken);
        }

        var notifier = new NotifierDouble();
        await RunJobAsync(TuesdayMorning, notifier);

        notifier.Calls.Should().BeEmpty();
    }

    // ---- the user's day and the user's morning ---------------------------------------------------

    /// <summary>
    /// Rule 6. At 23:30 UTC on Tuesday it is Wednesday 08:30 in Tokyo and Tuesday 16:30 in Los Angeles, so one
    /// run owes each of them a different day's digest. Paged one user per page, so the zones also cross a page.
    /// </summary>
    [Fact(DisplayName = "B4: 'today' is the calendar date in the user's own zone")]
    public async Task Today_IsTheUsersLocalDate()
    {
        const long tokyoUser = UserId + 30;
        const long laUser = UserId + 31;
        long tokyoWednesdayId, laTuesdayId;

        await using (var db = CreateDbContext())
        {
            await EnsureUserAsync(db, tokyoUser, TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo"), CancellationToken);
            await EnsureUserAsync(db, laUser, TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles"), CancellationToken);

            var tokyo = await SeedPeriodAsync(db, tokyoUser, lengthInDays: 7, ct: CancellationToken);
            tokyoWednesdayId = (await SeedItemAsync(db, tokyo, "Swim", days: [DayOfWeek.Wednesday], ct: CancellationToken)).Id;
            await SeedItemAsync(db, tokyo, "Gym", days: [DayOfWeek.Tuesday], ct: CancellationToken);

            var la = await SeedPeriodAsync(db, laUser, lengthInDays: 7, ct: CancellationToken);
            laTuesdayId = (await SeedItemAsync(db, la, "Gym", days: [DayOfWeek.Tuesday], ct: CancellationToken)).Id;
            await SeedItemAsync(db, la, "Swim", days: [DayOfWeek.Wednesday], ct: CancellationToken);
        }

        var notifier = new NotifierDouble();
        await RunJobAsync(new DateTimeOffset(2020, 9, 29, 23, 30, 0, TimeSpan.Zero), notifier, userPageSize: 1);

        notifier.Calls.Should().BeEquivalentTo([
            new NotifierDouble.DigestCall(tokyoUser, Tuesday.AddDays(1), [tokyoWednesdayId]),
            new NotifierDouble.DigestCall(laUser, Tuesday, [laTuesdayId])
        ]);
    }

    /// <summary>
    /// Rule 7: the day's own <c>WakeUpTime</c> when the calendar row exists, 08:00 otherwise. Also the one
    /// behavioural test on the <c>ICalendarDayLookup</c> read from Routines — a lookup that silently found no
    /// rows would fall back to 08:00 and pass every other case here.
    /// </summary>
    [Fact(DisplayName = "B4: the digest waits for the day's wake-up time, or 08:00 when the day is not planned")]
    public async Task Digest_WaitsForTheUsersMorning()
    {
        await using (var db = CreateDbContext())
        {
            var weekly = await SeedPeriodAsync(db, UserId, lengthInDays: 7, ct: CancellationToken);
            await SeedItemAsync(db, weekly, "Gym", days: [DayOfWeek.Tuesday], ct: CancellationToken);
        }

        var notifier = new NotifierDouble();
        var dawn = new DateTimeOffset(2020, 9, 29, 7, 45, 0, TimeSpan.Zero);

        await RunJobAsync(dawn, notifier);
        notifier.Calls.Should().BeEmpty("07:45 is before the default 08:00 morning");

        await using (var db = CreateDbContext())
            await SeedCalendarDayAsync(db, UserId, Tuesday, new TimeOnly(9, 30), CancellationToken);

        await RunJobAsync(dawn.AddMinutes(30), notifier);
        notifier.Calls.Should().BeEmpty("08:15 is past 08:00 but the planned day wakes up at 09:30");

        await RunJobAsync(dawn.AddHours(2), notifier);
        notifier.Calls.Should().ContainSingle("09:45 is past the planned wake-up");
    }

    /// <summary>Rule 12 — the same contract as <c>EndingSoonNotifiedFor</c> and <c>GraceNotifiedFor</c>.</summary>
    [Fact(DisplayName = "B4: a swallowed dispatch leaves the day unmarked, and a later run that morning retries")]
    public async Task SwallowedDispatch_IsRetried()
    {
        long weeklyId;
        await using (var db = CreateDbContext())
        {
            var weekly = await SeedPeriodAsync(db, UserId, lengthInDays: 7, ct: CancellationToken);
            weeklyId = weekly.Id;
            await SeedItemAsync(db, weekly, "Gym", days: [DayOfWeek.Tuesday], ct: CancellationToken);
        }

        var failing = new NotifierDouble { DispatchFails = true };
        await RunJobAsync(TuesdayMorning, failing);

        failing.Calls.Should().ContainSingle();
        (await ReadMarksAsync(UserId))[weeklyId].Should().BeNull();

        var healthy = new NotifierDouble();
        await RunJobAsync(TuesdayMorning.AddMinutes(15), healthy);

        healthy.Calls.Should().ContainSingle();
        (await ReadMarksAsync(UserId))[weeklyId].Should().Be(Tuesday);
    }

    // ---- the real pipeline -----------------------------------------------------------------------

    /// <summary>
    /// End to end: a notification row of the new type lands, with the payload the client's contract names, and
    /// the renderer derives the period as its subject because every item is in one period.
    /// </summary>
    [Fact(DisplayName = "B4: the digest lands as RoutineSuggestedToday, carrying the date, the items and the period subject")]
    public async Task RealPipeline_PersistsThePayload_AndSinglePeriodSubject()
    {
        long periodId;
        await using (var db = CreateDbContext())
        {
            var weekly = await SeedPeriodAsync(db, UserId, lengthInDays: 7, ct: CancellationToken);
            periodId = weekly.Id;
            await SeedItemAsync(db, weekly, "Gym", days: [DayOfWeek.Tuesday], ct: CancellationToken);
            await SeedItemAsync(db, weekly, "Stretch", days: [DayOfWeek.Tuesday], ct: CancellationToken);
        }

        await RunJobAsync(TuesdayMorning);

        var notification = await SingleDigestAsync();
        var payload = JsonDocument.Parse(notification.PayloadJson).RootElement;
        payload.GetProperty("date").GetString().Should().Be("2020-09-29");
        payload.GetProperty("count").GetInt32().Should().Be(2);
        payload.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("periodId").GetInt64())
            .Should().Equal(periodId, periodId);

        var renderer = Fixture.AdminAndUserFactory.Services.GetRequiredService<INotificationTextRenderer>();
        renderer.RenderSubject(notification.Type, notification.PayloadJson)
            .Should().Be(new NotificationSubject(NotificationSubjectKinds.RoutinePeriod, periodId));
    }

    [Fact(DisplayName = "B4: items in two periods carry no subject")]
    public async Task RealPipeline_ItemsInTwoPeriods_HaveNoSubject()
    {
        await using (var db = CreateDbContext())
        {
            var weekly = await SeedPeriodAsync(db, UserId, lengthInDays: 7, ct: CancellationToken);
            var monthly = await SeedPeriodAsync(db, UserId, lengthInDays: 30, ct: CancellationToken);
            await SeedItemAsync(db, weekly, "Gym", days: [DayOfWeek.Tuesday], ct: CancellationToken);
            await SeedItemAsync(db, monthly, "Pay rent", dayOfMonth: Tuesday.Day, ct: CancellationToken);
        }

        await RunJobAsync(TuesdayMorning);

        var notification = await SingleDigestAsync();
        var renderer = Fixture.AdminAndUserFactory.Services.GetRequiredService<INotificationTextRenderer>();
        renderer.RenderSubject(notification.Type, notification.PayloadJson).Should().BeNull();
    }

    /// <summary>The wording, in both tables — names first, then a count once there are more than three.</summary>
    [Theory(DisplayName = "B4: the body lists up to three names, then counts the rest, with Slovak plurals")]
    [InlineData(1, AvailableLocales.En, "Gym")]
    [InlineData(2, AvailableLocales.En, "Gym and Pay rent")]
    [InlineData(3, AvailableLocales.En, "Gym, Pay rent and Dentist")]
    [InlineData(4, AvailableLocales.En, "Gym, Pay rent and 2 more")]
    [InlineData(2, AvailableLocales.Sk, "Gym a Pay rent")]
    [InlineData(3, AvailableLocales.Sk, "Gym, Pay rent a Dentist")]
    [InlineData(4, AvailableLocales.Sk, "Gym, Pay rent a ďalšie 2")]
    [InlineData(7, AvailableLocales.Sk, "Gym, Pay rent a ďalších 5")]
    public void Body_ListsNames_ThenCounts(int count, AvailableLocales locale, string expected)
    {
        string[] names = ["Gym", "Pay rent", "Dentist", "Laundry", "Read", "Walk", "Call mum"];
        var payload = new
        {
            date = "2026-09-29",
            items = names.Take(count).Select((n, i) => new { itemId = i + 1, name = n, periodId = 7 }),
            count
        };

        var renderer = Fixture.AdminAndUserFactory.Services.GetRequiredService<INotificationTextRenderer>();
        var (title, body) = renderer.Render(NotificationType.RoutineSuggestedToday, JsonSerializer.Serialize(payload), locale);

        body.Should().Be(expected);
        title.Should().Be(locale == AvailableLocales.En ? "Suggested for today" : "Na dnes odporúčané");
    }

    /// <summary>
    /// The pinned day's digest. Filtered on the payload date too: a real-clock fire from another test host (see
    /// the class summary) may add a digest for the real today, and that one is not under test.
    /// </summary>
    private async Task<Notification> SingleDigestAsync()
    {
        await using var db = CreateDbContext();
        var digests = await db.Set<Notification>().AsNoTracking()
            .Where(n => n.UserId == UserId && n.Type == NotificationType.RoutineSuggestedToday)
            .ToListAsync(CancellationToken);

        return digests.Should().ContainSingle(n =>
            JsonDocument.Parse(n.PayloadJson).RootElement.GetProperty("date").GetString() == Tuesday.ToString("yyyy-MM-dd")).Subject;
    }
}
