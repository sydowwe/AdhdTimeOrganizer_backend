using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using AdhdTimeOrganizer.application.service.morningBriefing;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.Core.domain.model.@enum;
using AdhdTimeOrganizer.infrastructure.jobs;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sydowwe.Framework.Contracts.notification;
using Sydowwe.Framework.Contracts.notification.payload;
using Sydowwe.Framework.Contracts.scheduling;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.domain.extServiceContract.user;
using Sydowwe.Framework.Testing;
using Sydowwe.Notifications.application;
using Sydowwe.Notifications.domain.entity;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// P2-01 — <see cref="MorningBriefingJobHandler"/>, one notification per user per morning instead of a push per
/// signal. It runs with nobody watching, and both of its mistakes are silent: a briefing too many is the fatigue it
/// exists to prevent, one too few is nothing at all. So every case asserts on what the notifier was handed and on the
/// mark left on the user row; one group runs the real pipeline and reads the persisted notification.
/// <para>
/// <b>The clock is pinned to Tuesday 29 September 2020</b>, for the reason <c>RoutineSuggestedTodayJobTests</c> gives:
/// Quartz runs this job every quarter hour inside every test host with the <i>real</i> clock, and only a date no real
/// clock will produce keeps those fires off the rows under test. The fixture user is on UTC, so "08:00" below is both
/// the user's wall clock and the instant.
/// </para>
/// </summary>
[Collection("Postgres")]
public class MorningBriefingJobTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    private static readonly DateOnly Tuesday = new(2020, 9, 29);

    private static DateTimeOffset At(int hour, int minute = 0, DateOnly? date = null) =>
        new((date ?? Tuesday).ToDateTime(new TimeOnly(hour, minute)), TimeSpan.Zero);

    // ---- test doubles -----------------------------------------------------------------------------

    private sealed class NotifierDouble : IMorningBriefingNotifier
    {
        public List<(long UserId, MorningBriefingPayload Payload)> Calls { get; } = [];

        /// <summary>Mirrors the real implementation swallowing a dispatch failure: recorded, and reported as not sent.</summary>
        public bool DispatchFails { get; set; }

        public Task<bool> NotifyAsync(long userId, MorningBriefingPayload payload, CancellationToken ct)
        {
            Calls.Add((userId, payload));
            return Task.FromResult(!DispatchFails);
        }
    }

    /// <summary>The real background scope has no user. See <c>RoutineSuggestedTodayJobTests</c> for why the fake user cannot stand in.</summary>
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

    // ---- what it says -----------------------------------------------------------------------------

    /// <summary>
    /// Every section, each with the rows that must <i>not</i> count beside the ones that must: a done, background or
    /// cancelled block, a block from before the carry-over week, a timed or done or future to-do, a done or
    /// another-day routine item.
    /// </summary>
    [Fact(DisplayName = "P2-01: one briefing at wake-up, counting each section by its slice's own rule")]
    public async Task Briefing_CountsEverySection()
    {
        // Today, 07:00 wake-up (the seeded calendar's). 07:30 is behind the 08:30 run, so it counts but is not "first".
        await SeedBlockAsync(UserId, Tuesday, new TimeOnly(7, 30), new TimeOnly(8, 0));
        await SeedBlockAsync(UserId, Tuesday, new TimeOnly(9, 0), new TimeOnly(10, 0));
        await SeedBlockAsync(UserId, Tuesday, new TimeOnly(11, 0), new TimeOnly(12, 0), PlannerTaskStatus.InProgress);
        await SeedBlockAsync(UserId, Tuesday, new TimeOnly(8, 45), new TimeOnly(9, 0), background: true);
        await SeedBlockAsync(UserId, Tuesday, new TimeOnly(8, 45), new TimeOnly(9, 0), PlannerTaskStatus.Completed);

        // Carry-over: yesterday and five days ago count; a cancelled one and one from nine days ago do not.
        await SeedBlockAsync(UserId, Tuesday.AddDays(-1), new TimeOnly(9, 0), new TimeOnly(10, 0));
        await SeedBlockAsync(UserId, Tuesday.AddDays(-5), new TimeOnly(9, 0), new TimeOnly(10, 0), PlannerTaskStatus.InProgress);
        await SeedBlockAsync(UserId, Tuesday.AddDays(-1), new TimeOnly(11, 0), new TimeOnly(12, 0), PlannerTaskStatus.Cancelled);
        await SeedBlockAsync(UserId, Tuesday.AddDays(-9), new TimeOnly(9, 0), new TimeOnly(10, 0));

        await SeedTodoAsync(UserId, Tuesday);
        await SeedTodoAsync(UserId, Tuesday);
        await SeedTodoAsync(UserId, Tuesday, dueTime: new TimeOnly(15, 0)); // has its own reminder
        await SeedTodoAsync(UserId, Tuesday.AddDays(-1));
        await SeedTodoAsync(UserId, Tuesday.AddDays(-40), dueTime: new TimeOnly(9, 0));
        await SeedTodoAsync(UserId, Tuesday.AddDays(-1), isDone: true);
        await SeedTodoAsync(UserId, Tuesday.AddDays(1));

        await SeedRoutineAsync(UserId, (DayOfWeek.Tuesday, false), (DayOfWeek.Tuesday, true), (DayOfWeek.Thursday, false));

        var notifier = new NotifierDouble();
        await RunJobAsync(At(8, 30), notifier);

        notifier.Calls.Should().ContainSingle().Which.Should().Be((UserId, new MorningBriefingPayload(
            Tuesday, Blocks: 3, FirstBlockAt: new TimeOnly(9, 0), CarryOver: 2, DueToday: 2, Overdue: 2, RoutinesSuggested: 1)));
        (await ReadMarkAsync(UserId)).Should().Be(Tuesday);
    }

    [Fact(DisplayName = "P2-01: a second run the same morning sends nothing")]
    public async Task Briefing_IsSentOncePerDay()
    {
        await SeedTodoAsync(UserId, Tuesday);

        var notifier = new NotifierDouble();
        await RunJobAsync(At(8, 0), notifier);
        await RunJobAsync(At(8, 15), notifier);

        // A to-do added after the briefing went out does not reopen the morning either.
        await SeedTodoAsync(UserId, Tuesday);
        await RunJobAsync(At(8, 30), notifier);

        notifier.Calls.Should().ContainSingle();

        // Wednesday is a new morning.
        await RunJobAsync(At(8, 0, Tuesday.AddDays(1)), notifier);
        notifier.Calls.Should().HaveCount(2).And.Subject.Last().Payload.Should().Be(new MorningBriefingPayload(Tuesday.AddDays(1), Overdue: 2));
    }

    [Fact(DisplayName = "P2-01: a morning with nothing to say sends nothing, and is decided — a block planned later earns no push")]
    public async Task EmptyMorning_SendsNothing_AndIsDecided()
    {
        var notifier = new NotifierDouble();
        await RunJobAsync(At(8, 0), notifier);

        notifier.Calls.Should().BeEmpty();
        (await ReadMarkAsync(UserId)).Should().Be(Tuesday);

        await SeedBlockAsync(UserId, Tuesday, new TimeOnly(15, 0), new TimeOnly(16, 0));
        await RunJobAsync(At(8, 15), notifier);
        notifier.Calls.Should().BeEmpty("the morning was already decided; a 'morning' briefing at noon is noise");
    }

    [Fact(DisplayName = "P2-01: no plan, but something due — the briefing goes, with no blocks and no first time")]
    public async Task NoPlan_ButSomethingDue_IsSent()
    {
        await SeedTodoAsync(UserId, Tuesday.AddDays(-3));

        var notifier = new NotifierDouble();
        await RunJobAsync(At(8, 0), notifier);

        notifier.Calls.Should().ContainSingle().Which.Payload.Should().Be(new MorningBriefingPayload(Tuesday, Overdue: 1));
    }

    // ---- when -------------------------------------------------------------------------------------

    /// <summary>
    /// The same resolution the routine digest uses — the day's own wake-up, or the user's default wake-up (FO10) with
    /// no calendar row — plus the user's offset. Also the one behavioural pin on the calendar read: one that found no
    /// rows would fall back to the default and pass every other case here.
    /// </summary>
    [Fact(DisplayName = "P2-01: the briefing waits for the day's wake-up plus the user's offset, or the user's default wake-up with no calendar row")]
    public async Task Briefing_WaitsForTheUsersMorning()
    {
        await SeedTodoAsync(UserId, Tuesday.AddDays(-1));

        var notifier = new NotifierDouble();
        await RunJobAsync(At(7, 45), notifier);
        notifier.Calls.Should().BeEmpty("07:45 is before the default 08:00 morning");

        await using (var db = CreateDbContext())
        {
            await PlanningTestSeedHelper.SeedCalendarAsync(db, Tuesday, UserId, CancellationToken);
            await db.Set<Calendar>().Where(c => c.UserId == UserId && c.Date == Tuesday)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.WakeUpTime, new TimeOnly(9, 0)), CancellationToken);
            await db.Set<User>().Where(u => u.Id == UserId)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.MorningBriefingOffsetMinutes, 30), CancellationToken);
        }

        await RunJobAsync(At(9, 15), notifier);
        notifier.Calls.Should().BeEmpty("the day wakes up at 09:00 and the user asked for 30 minutes after");

        await RunJobAsync(At(9, 30), notifier);
        notifier.Calls.Should().ContainSingle();
    }

    [Fact(DisplayName = "P2-01: with no calendar row the briefing waits for the user's own default wake-up (FO10)")]
    public async Task NoCalendarRow_WaitsForTheUsersDefaultWakeUp()
    {
        await SeedTodoAsync(UserId, Tuesday.AddDays(-1));
        await using (var db = CreateDbContext())
            await db.Set<User>().Where(u => u.Id == UserId)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.DefaultWakeUpTime, new TimeOnly(6, 30)), CancellationToken);

        var notifier = new NotifierDouble();
        await RunJobAsync(At(6, 15), notifier);
        notifier.Calls.Should().BeEmpty("06:15 is before the user's 06:30");

        await RunJobAsync(At(6, 30), notifier);
        notifier.Calls.Should().ContainSingle("the user's own default, not 08:00");
    }

    /// <summary>
    /// At 15:30 UTC on Tuesday it is 08:30 Tuesday in Los Angeles and 00:30 Wednesday in Tokyo; at 23:30 UTC it is
    /// 16:30 Tuesday and 08:30 Wednesday. Each gets their own day's briefing, at their own morning, counted on their own
    /// date — the to-do due Wednesday is Tokyo's "due today" and nobody's overdue. Paged one user per page.
    /// </summary>
    [Fact(DisplayName = "P2-01: two zones — each user's briefing comes at their own morning, about their own today")]
    public async Task TwoZones_EachGetTheirOwnMorning()
    {
        const long tokyoUser = UserId + 40;
        const long laUser = UserId + 41;

        await using (var db = CreateDbContext())
        {
            await EnsureUserAsync(db, tokyoUser, TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo"), CancellationToken);
            await EnsureUserAsync(db, laUser, TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles"), CancellationToken);
        }

        await SeedTodoAsync(tokyoUser, Tuesday.AddDays(1));
        await SeedTodoAsync(laUser, Tuesday);
        await SeedTodoAsync(laUser, Tuesday.AddDays(1));

        var notifier = new NotifierDouble();
        await RunJobAsync(At(15, 30), notifier, userPageSize: 1);

        notifier.Calls.Should().ContainSingle().Which.Should().Be((laUser, new MorningBriefingPayload(Tuesday, DueToday: 1)));

        await RunJobAsync(At(23, 30), notifier, userPageSize: 1);

        notifier.Calls.Should().HaveCount(2);
        notifier.Calls[1].Should().Be((tokyoUser, new MorningBriefingPayload(Tuesday.AddDays(1), DueToday: 1)));
        (await ReadMarkAsync(laUser)).Should().Be(Tuesday);
        (await ReadMarkAsync(tokyoUser)).Should().Be(Tuesday.AddDays(1));
    }

    // ---- and when not ----------------------------------------------------------------------------

    [Theory(DisplayName = "P2-01: no briefing on an away day")]
    [InlineData(DayType.Vacation)]
    [InlineData(DayType.SickDay)]
    public async Task AwayDay_SendsNothing(DayType dayType)
    {
        await SeedBlockAsync(UserId, Tuesday, new TimeOnly(9, 0), new TimeOnly(10, 0), dayType: dayType);
        await SeedTodoAsync(UserId, Tuesday.AddDays(-1));

        var notifier = new NotifierDouble();
        await RunJobAsync(At(8, 0), notifier);

        notifier.Calls.Should().BeEmpty();
    }

    /// <summary>The welcome-back card already shows a returning user the pile; a briefing beside it says it twice.</summary>
    [Fact(DisplayName = "P2-01: a user with the welcome-back card open gets no briefing that morning")]
    public async Task WelcomeBackCardOpen_SendsNothing()
    {
        await SeedTodoAsync(UserId, Tuesday.AddDays(-10));
        await using (var db = CreateDbContext())
            await db.Set<User>().Where(u => u.Id == UserId).ExecuteUpdateAsync(s => s
                .SetProperty(u => u.WelcomeBackAwayFrom, At(8, 0).UtcDateTime.AddDays(-10))
                .SetProperty(u => u.WelcomeBackReturnedAt, At(7, 0).UtcDateTime), CancellationToken);

        var notifier = new NotifierDouble();
        await RunJobAsync(At(8, 0), notifier);

        notifier.Calls.Should().BeEmpty();
        (await ReadMarkAsync(UserId)).Should().Be(Tuesday);
    }

    [Fact(DisplayName = "P2-01: a user who turned the briefing off gets none")]
    public async Task BriefingOff_SendsNothing()
    {
        await SeedTodoAsync(UserId, Tuesday.AddDays(-1));
        await using (var db = CreateDbContext())
            await db.Set<User>().Where(u => u.Id == UserId)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.MorningBriefingEnabled, false), CancellationToken);

        var notifier = new NotifierDouble();
        await RunJobAsync(At(8, 0), notifier);

        notifier.Calls.Should().BeEmpty();
        (await ReadMarkAsync(UserId)).Should().BeNull();
    }

    [Fact(DisplayName = "P2-01: a swallowed dispatch leaves the morning open and a later run retries — within the morning only")]
    public async Task SwallowedDispatch_IsRetried_WithinTheMorning()
    {
        await SeedTodoAsync(UserId, Tuesday.AddDays(-1));

        var failing = new NotifierDouble { DispatchFails = true };
        await RunJobAsync(At(8, 0), failing);
        failing.Calls.Should().ContainSingle();
        (await ReadMarkAsync(UserId)).Should().BeNull();

        var healthy = new NotifierDouble();
        await RunJobAsync(At(8, 15), healthy);
        healthy.Calls.Should().ContainSingle();
        (await ReadMarkAsync(UserId)).Should().Be(Tuesday);

        // Wednesday's first chance comes after the morning is gone: decided without a send.
        await RunJobAsync(new DateTimeOffset(Tuesday.AddDays(1).ToDateTime(new TimeOnly(8, 0)), TimeSpan.Zero) + MorningBriefingJobHandler.LateBy, healthy);
        healthy.Calls.Should().ContainSingle("a 'morning' briefing in the afternoon is not one");
        (await ReadMarkAsync(UserId)).Should().Be(Tuesday.AddDays(1));
    }

    // ---- the real pipeline -----------------------------------------------------------------------

    /// <summary>
    /// End to end: the row lands as <see cref="PortalNotificationTypes.MorningBriefing"/> with the payload the client's
    /// contract names, it links to no single row, and the renderer reads it in the user's language.
    /// </summary>
    [Fact(DisplayName = "P2-01: the briefing lands as MorningBriefing, carrying the date and the counts")]
    public async Task RealPipeline_PersistsThePayload()
    {
        await SeedBlockAsync(UserId, Tuesday, new TimeOnly(9, 0), new TimeOnly(10, 0));
        await SeedTodoAsync(UserId, Tuesday.AddDays(-2));

        await RunJobAsync(At(8, 0));

        var notification = await SingleBriefingAsync();
        var payload = JsonDocument.Parse(notification.PayloadJson).RootElement;
        payload.GetProperty("date").GetString().Should().Be("2020-09-29");
        payload.GetProperty("blocks").GetInt32().Should().Be(1);
        payload.GetProperty("firstBlockAt").GetString().Should().Be("09:00:00");
        payload.GetProperty("overdue").GetInt32().Should().Be(1);

        var renderer = Fixture.AdminAndUserFactory.Services.GetRequiredService<INotificationTextRenderer>();
        renderer.RenderSubject(notification.Type, notification.PayloadJson).Should().BeNull();
        renderer.Render(notification.Type, notification.PayloadJson, AvailableLocales.En)
            .Should().Be(("Your day", "1 block planned, first at 09:00 · 1 overdue"));
    }

    /// <summary>The wording in both tables: the plan first ("no plan yet" without one), then each non-zero section.</summary>
    [Theory(DisplayName = "P2-01: the body names the plan, then each non-zero section, with Slovak plurals")]
    [InlineData(AvailableLocales.En, 4, "09:00", 2, 3, 1, 2,
        "4 blocks planned, first at 09:00 · 2 to carry over · 3 to-dos due today · 1 overdue · 2 routine tasks for today")]
    [InlineData(AvailableLocales.En, 1, null, 0, 1, 0, 1, "1 block planned · 1 to-do due today · 1 routine task for today")]
    [InlineData(AvailableLocales.En, 0, null, 0, 0, 5, 0, "No plan yet · 5 overdue")]
    [InlineData(AvailableLocales.Sk, 4, "09:00", 2, 3, 1, 2,
        "4 naplánované bloky, prvý o 09:00 · 2 na presunutie · 3 úlohy s termínom dnes · 1 po termíne · 2 úlohy z rutín na dnes")]
    [InlineData(AvailableLocales.Sk, 1, null, 0, 1, 0, 5, "1 naplánovaný blok · 1 úloha s termínom dnes · 5 úloh z rutín na dnes")]
    [InlineData(AvailableLocales.Sk, 0, null, 0, 0, 5, 0, "Zatiaľ bez plánu · 5 po termíne")]
    public void Body_NamesThePlanThenEachSection(
        AvailableLocales locale, int blocks, string? firstAt, int carryOver, int dueToday, int overdue, int routines, string expected)
    {
        var json = JsonSerializer.Serialize(new { date = "2020-09-29", blocks, firstBlockAt = firstAt is null ? null : firstAt + ":00", carryOver, dueToday, overdue, routinesSuggested = routines });

        var renderer = Fixture.AdminAndUserFactory.Services.GetRequiredService<INotificationTextRenderer>();
        renderer.Render(PortalNotificationTypes.MorningBriefing, json, locale).Body.Should().Be(expected);
    }

    [Fact(DisplayName = "P2-01: an all-zero or unreadable payload renders the name-less sentence, not a lone 'no plan yet'")]
    public void EmptyPayload_RendersTheFallback()
    {
        var renderer = Fixture.AdminAndUserFactory.Services.GetRequiredService<INotificationTextRenderer>();

        renderer.Render(PortalNotificationTypes.MorningBriefing, """{"date":"2020-09-29"}""", AvailableLocales.En).Body.Should().Be("Here's what today holds.");
        renderer.Render(PortalNotificationTypes.MorningBriefing, "not json", AvailableLocales.Sk).Body.Should().Be("Toto vás dnes čaká.");
    }

    // ---- the setting -----------------------------------------------------------------------------

    [Fact(DisplayName = "P2-01: the briefing switch and offset round-trip through the preferences endpoint; on by default")]
    public async Task Setting_RoundTripsThroughThePreferencesEndpoint()
    {
        var client = CreateClient();

        var initial = await client.GetFromJsonAsync<UserData>("/api/user/data", JsonOpts, CancellationToken);
        initial!.MorningBriefingEnabled.Should().BeTrue();
        initial.MorningBriefingOffsetMinutes.Should().Be(0);

        (await client.PutAsJsonAsync("/api/user/preferences", new { MorningBriefingEnabled = false, MorningBriefingOffsetMinutes = 45 }, JsonOpts, CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetFromJsonAsync<UserData>("/api/user/data", JsonOpts, CancellationToken))
            .Should().BeEquivalentTo(new { MorningBriefingEnabled = false, MorningBriefingOffsetMinutes = 45 });

        // Null leaves both alone, as for every other preference.
        (await client.PutAsJsonAsync("/api/user/preferences", new { FirstDayOfWeek = 0 }, JsonOpts, CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetFromJsonAsync<UserData>("/api/user/data", JsonOpts, CancellationToken))
            .Should().BeEquivalentTo(new { MorningBriefingEnabled = false, MorningBriefingOffsetMinutes = 45 });

        (await client.PutAsJsonAsync("/api/user/preferences", new { MorningBriefingOffsetMinutes = User.MorningBriefingMaxOffsetMinutes + 1 }, JsonOpts, CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PutAsJsonAsync("/api/user/preferences", new { MorningBriefingOffsetMinutes = -1 }, JsonOpts, CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---- helpers ----------------------------------------------------------------------------------

    private record UserData(long Id, bool MorningBriefingEnabled, int MorningBriefingOffsetMinutes);

    private async Task SeedBlockAsync(
        long userId,
        DateOnly date,
        TimeOnly start,
        TimeOnly end,
        PlannerTaskStatus status = PlannerTaskStatus.NotStarted,
        bool background = false,
        DayType dayType = DayType.Workday)
    {
        await using var db = CreateDbContext();
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, $"block-{Guid.NewGuid():N}", userId, CancellationToken);
        var calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, date, userId, CancellationToken, dayType);
        var taskId = await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, calendarId, start, end, userId, status, ct: CancellationToken);

        if (background)
            await db.Set<PlannerTask>().IgnoreQueryFilters().Where(t => t.Id == taskId)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.IsBackground, true), CancellationToken);
    }

    private async Task SeedTodoAsync(long userId, DateOnly dueDate, TimeOnly? dueTime = null, bool isDone = false)
    {
        await using var db = CreateDbContext();
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, $"todo-{Guid.NewGuid():N}", userId, CancellationToken);

        // One per user: (user, priority) is unique.
        var priority = await db.Set<TaskPriority>().IgnoreQueryFilters().FirstOrDefaultAsync(p => p.UserId == userId, CancellationToken);
        if (priority is null)
        {
            priority = new TaskPriority { UserId = userId, Text = $"Priority {Guid.NewGuid():N}", Color = "#445566", Priority = 1 };
            db.Set<TaskPriority>().Add(priority);
            await db.SaveChangesAsync(CancellationToken);
        }

        db.Set<TodoListItem>().Add(new TodoListItem
        {
            UserId = userId,
            ActivityId = activityId,
            TaskPriorityId = priority.Id,
            DueDate = dueDate,
            DueTime = dueTime,
            IsDone = isDone
        });
        await db.SaveChangesAsync(CancellationToken);
    }

    /// <summary>A weekly period with one item per (weekday, done) pair.</summary>
    private async Task SeedRoutineAsync(long userId, params (DayOfWeek Day, bool IsDone)[] items)
    {
        await using var db = CreateDbContext();
        var period = new RoutineTimePeriod
        {
            UserId = userId,
            Text = $"Period {Guid.NewGuid():N}",
            Color = "#123456",
            LengthInDays = 7,
            ResetAnchorDay = 0,
            StreakThreshold = 100,
            StreakGraceDays = 0,
            // Recent enough that no reset is due at the pinned clock, so a tick is this cycle's.
            LastResetAt = At(8, 0).UtcDateTime.AddDays(-1)
        };
        db.Set<RoutineTimePeriod>().Add(period);
        await db.SaveChangesAsync(CancellationToken);

        foreach (var (day, isDone) in items)
        {
            var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, $"routine-{Guid.NewGuid():N}", userId, CancellationToken);
            db.Set<RoutineTodoList>().Add(new RoutineTodoList
            {
                UserId = userId,
                ActivityId = activityId,
                TimePeriodId = period.Id,
                IsDone = isDone,
                SuggestedDays = [day]
            });
            await db.SaveChangesAsync(CancellationToken);
        }
    }

    private static async Task EnsureUserAsync(DbContext db, long userId, TimeZoneInfo timezone, CancellationToken ct)
    {
        var email = $"briefing-{userId}@test.com";
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

    private async Task<DateOnly?> ReadMarkAsync(long userId)
    {
        await using var db = CreateDbContext();
        return await db.Set<User>().AsNoTracking().Where(u => u.Id == userId)
            .Select(u => u.MorningBriefingHandledFor).SingleAsync(CancellationToken);
    }

    /// <summary>
    /// The pinned day's briefing. Filtered on the payload date too: a real-clock fire from another test host (see the
    /// class summary) may add one for the real today, and that one is not under test.
    /// </summary>
    private async Task<Notification> SingleBriefingAsync()
    {
        await using var db = CreateDbContext();
        var briefings = await db.Set<Notification>().AsNoTracking()
            .Where(n => n.UserId == UserId && n.Type == PortalNotificationTypes.MorningBriefing)
            .ToListAsync(CancellationToken);

        return briefings.Should().ContainSingle(n =>
            JsonDocument.Parse(n.PayloadJson).RootElement.GetProperty("date").GetString() == Tuesday.ToString("yyyy-MM-dd")).Subject;
    }

    /// <summary>
    /// Runs the job the way the Scheduler dispatcher does — one scope per fire, no ambient user — with the clock pinned
    /// at <paramref name="now"/>, and the notifier substituted when a double is given.
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
                services.RemoveAll<IMorningBriefingNotifier>();
                services.AddSingleton<IMorningBriefingNotifier>(notifier);
            }
        });

        await using var scope = factory.Services.CreateAsyncScope();
        var handler = (IScheduledJobHandler)(userPageSize is { } size
            ? ActivatorUtilities.CreateInstance(scope.ServiceProvider, typeof(MorningBriefingJobHandler), size)
            : ActivatorUtilities.CreateInstance(scope.ServiceProvider, typeof(MorningBriefingJobHandler)));

        await handler.ExecuteAsync(new ScheduledJobContext
        {
            ScheduledFireTimeUtc = now.UtcDateTime,
            ActualFireTimeUtc = now.UtcDateTime,
            JobKey = handler.Key,
            CorrelationId = "morning-briefing-test",
            TriggerSource = TriggerSource.Manual
        }, CancellationToken);
    }
}
