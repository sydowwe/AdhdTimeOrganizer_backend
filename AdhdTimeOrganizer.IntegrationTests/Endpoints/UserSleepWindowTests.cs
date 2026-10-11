using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.Core.domain.model.@enum;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using AdhdTimeOrganizer.Planning.infrastructure.persistence.seeder.userDefault;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.infrastructure.persistence.seeder.@interface;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// FO10 — the user's default sleep window (<c>User.DefaultWakeUpTime</c> / <c>DefaultBedTime</c>) and the days that
/// inherit it.
/// <para>
/// What fails silently here: the calendar is pre-seeded two years ahead, so a default that only reached <i>new</i>
/// rows would save, read back and look right in settings while every day the user actually plans kept 08:00 / 00:00.
/// And the reverse — a sync that ignored <c>HasCustomSleepWindow</c> — would quietly overwrite a window the user set
/// on one day. Both answer 204, so every case asserts on the stored calendar rows.
/// </para>
/// </summary>
[Collection("Postgres")]
public class UserSleepWindowTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;
    private const string Password = "Test@1234!";

    private static readonly TimeOnly OldWake = new(8, 0);
    private static readonly TimeOnly OldBed = new(0, 0);
    private static readonly TimeOnly NewWake = new(6, 30);
    private static readonly TimeOnly NewBed = new(22, 45);

    [Fact]
    public async Task TheWindow_RoundTripsThroughThePreferencesEndpoint_AndAnUnrelatedPutLeavesIt()
    {
        var client = CreateClient();

        (await client.GetFromJsonAsync<UserData>("/api/user/data", JsonOpts, CancellationToken))!
            .Should().BeEquivalentTo(new { DefaultWakeUpTime = new TimeData(8, 0), DefaultBedTime = new TimeData(0, 0) },
                "a user who never set it starts on the window every day used to be created with");

        (await PutWindowAsync(client, NewWake, NewBed)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.PutAsJsonAsync("/api/user/preferences", new { FirstDayOfWeek = 0 }, JsonOpts, CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await client.GetFromJsonAsync<UserData>("/api/user/data", JsonOpts, CancellationToken))!
            .Should().BeEquivalentTo(new { DefaultWakeUpTime = new TimeData(6, 30), DefaultBedTime = new TimeData(22, 45) },
                "stored, readable back, and not reset by a PUT that did not send it");
    }

    [Theory]
    [InlineData(24, 0)]
    [InlineData(7, 60)]
    [InlineData(-1, 0)]
    public async Task AnOutOfRangeTime_Is400_AndChangesNothing(int hours, int minutes)
    {
        var response = await CreateClient().PutAsJsonAsync("/api/user/preferences",
            new { DefaultWakeUpTime = new { Hours = hours, Minutes = minutes } }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync(CancellationToken));
        (await StoredWindowAsync()).Should().Be(UserSleepWindow.Fallback);
    }

    /// <summary>
    /// The heart of it: a changed default moves today and every later untouched day, and nothing else. A past day is
    /// history; a day whose window was chosen for it keeps it.
    /// </summary>
    [Fact]
    public async Task ChangingTheDefault_MovesUntouchedDaysFromToday_AndLeavesCustomAndPastDays()
    {
        var today = await UserTodayAsync();
        var past = await SeedDayAsync(today.AddDays(-2), OldWake, OldBed);
        var todayRow = await SeedDayAsync(today, OldWake, OldBed);
        var future = await SeedDayAsync(today.AddDays(2), OldWake, OldBed);
        var custom = await SeedDayAsync(today.AddDays(3), new TimeOnly(5, 0), new TimeOnly(21, 0), custom: true);

        (await PutWindowAsync(CreateClient(), NewWake, NewBed)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await WindowOfAsync(todayRow)).Should().Be((NewWake, NewBed, false));
        (await WindowOfAsync(future)).Should().Be((NewWake, NewBed, false), "a day nobody touched follows the default");
        (await WindowOfAsync(past)).Should().Be((OldWake, OldBed, false), "a past day is history and is never rewritten");
        (await WindowOfAsync(custom)).Should().Be((new TimeOnly(5, 0), new TimeOnly(21, 0), true),
            "a window chosen for one day belongs to that day");
    }

    /// <summary>
    /// Only the half that was sent changes: the PUT's "null leaves it unchanged" convention reaches the days too, so
    /// moving the wake-up alone does not reset an existing bed time.
    /// </summary>
    [Fact]
    public async Task SendingOnlyTheWakeUp_KeepsTheBedTime_OnTheUserAndOnTheDays()
    {
        var today = await UserTodayAsync();
        (await PutWindowAsync(CreateClient(), NewWake, NewBed)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var day = await SeedDayAsync(today.AddDays(5), NewWake, NewBed);

        (await CreateClient().PutAsJsonAsync("/api/user/preferences",
                new { DefaultWakeUpTime = new { Hours = 9, Minutes = 15 } }, JsonOpts, CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await StoredWindowAsync()).Should().Be(new UserSleepWindow(new TimeOnly(9, 15), NewBed));
        (await WindowOfAsync(day)).Should().Be((new TimeOnly(9, 15), NewBed, false));
    }

    /// <summary>
    /// The day editor PUTs the whole row. Re-sending the inherited window while changing something else is not a
    /// choice and must not pin the day; changing the window is, and must.
    /// </summary>
    [Fact]
    public async Task TheDayEditor_MarksTheDayOnlyWhenItsWindowActuallyChanges()
    {
        var today = await UserTodayAsync();
        var untouched = await SeedDayAsync(today.AddDays(4), OldWake, OldBed);
        var edited = await SeedDayAsync(today.AddDays(6), OldWake, OldBed);

        (await PutDayAsync(untouched, OldWake, OldBed)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PutDayAsync(edited, new TimeOnly(7, 10), new TimeOnly(23, 20))).StatusCode.Should().Be(HttpStatusCode.OK);

        (await WindowOfAsync(untouched)).Should().Be((OldWake, OldBed, false), "only the label changed");
        (await WindowOfAsync(edited)).Should().Be((new TimeOnly(7, 10), new TimeOnly(23, 20), true));

        (await PutWindowAsync(CreateClient(), NewWake, NewBed)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await WindowOfAsync(untouched)).Should().Be((NewWake, NewBed, false), "a label edit did not take the day off the default");
        (await WindowOfAsync(edited)).Should().Be((new TimeOnly(7, 10), new TimeOnly(23, 20), true));
    }

    /// <summary>The lazy creator: a day past the seeded horizon starts on the user's window, unmarked.</summary>
    [Fact]
    public async Task ALazilyCreatedDay_StartsOnTheUsersWindow()
    {
        (await PutWindowAsync(CreateClient(), NewWake, NewBed)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var activityId = await SeedActivityAsync("fo10-lazy");
        var date = new DateOnly(2033, 6, 14);

        var response = await CreateClient().PostAsJsonAsync("/api/planner-task", new
        {
            StartTime = new { Hours = 9, Minutes = 0 },
            EndTime = new { Hours = 10, Minutes = 0 },
            IsBackground = false,
            ActivityId = activityId,
            Status = "NotStarted",
            Date = date
        }, JsonOpts, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(CancellationToken));

        await using var db = CreateDbContext();
        var day = await db.Set<Calendar>().IgnoreQueryFilters()
            .SingleAsync(c => c.UserId == UserId && c.Date == date, CancellationToken);
        (day.WakeUpTime, day.BedTime, day.HasCustomSleepWindow).Should().Be((NewWake, NewBed, false));
    }

    /// <summary>The bulk creator: the seeder stamps the user's window on every day it fills.</summary>
    [Fact]
    public async Task TheSeeder_FillsDaysWithTheUsersWindow()
    {
        var userId = await CreateUserAsync(NewWake, NewBed);

        using (var scope = Fixture.UnauthenticatedFactory.Services.CreateScope())
            await scope.ServiceProvider.GetServices<IPerUserDefaultSeeder>().OfType<CalendarSeeder>().Single()
                .SetupDefaults(userId, CancellationToken);

        await using var db = CreateDbContext();
        var windows = await db.Set<Calendar>().IgnoreQueryFilters()
            .Where(c => c.UserId == userId)
            .Select(c => new { c.WakeUpTime, c.BedTime, c.HasCustomSleepWindow })
            .Distinct()
            .ToListAsync(CancellationToken);

        windows.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new { WakeUpTime = NewWake, BedTime = NewBed, HasCustomSleepWindow = false });
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private Task<HttpResponseMessage> PutWindowAsync(HttpClient client, TimeOnly wake, TimeOnly bed) =>
        client.PutAsJsonAsync("/api/user/preferences", new
        {
            DefaultWakeUpTime = new { Hours = wake.Hour, Minutes = wake.Minute },
            DefaultBedTime = new { Hours = bed.Hour, Minutes = bed.Minute }
        }, JsonOpts, CancellationToken);

    private Task<HttpResponseMessage> PutDayAsync(long calendarId, TimeOnly wake, TimeOnly bed) =>
        CreateClient().PutAsJsonAsync($"/api/calendar/{calendarId}", new
        {
            // Special: legal on any weekday (CalendarDayTypeRule) and not an away day, so the date the clock picks
            // cannot make the PUT a 400.
            DayType = DayType.Special.ToString(),
            Label = "Edited",
            WakeUpTime = new { Hours = wake.Hour, Minutes = wake.Minute },
            BedTime = new { Hours = bed.Hour, Minutes = bed.Minute }
        }, JsonOpts, CancellationToken);

    private async Task<DateOnly> UserTodayAsync()
    {
        await using var db = CreateDbContext();
        var zone = await db.Set<User>().IgnoreQueryFilters()
            .Where(u => u.Id == UserId).Select(u => u.Timezone).SingleAsync(CancellationToken);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone));
    }

    private async Task<UserSleepWindow> StoredWindowAsync()
    {
        await using var db = CreateDbContext();
        return await UserSleepWindow.ForUserAsync(db, UserId, CancellationToken);
    }

    private async Task<long> SeedDayAsync(DateOnly date, TimeOnly wake, TimeOnly bed, bool custom = false)
    {
        await using var db = CreateDbContext();
        var existing = await db.Set<Calendar>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.UserId == UserId && c.Date == date, CancellationToken);
        if (existing is not null)
            db.Remove(existing);

        var day = new Calendar
        {
            UserId = UserId,
            Date = date,
            DayType = DayType.Special,
            WakeUpTime = wake,
            BedTime = bed,
            HasCustomSleepWindow = custom
        };
        db.Set<Calendar>().Add(day);
        await db.SaveChangesAsync(CancellationToken);
        return day.Id;
    }

    private async Task<(TimeOnly Wake, TimeOnly Bed, bool Custom)> WindowOfAsync(long calendarId)
    {
        await using var db = CreateDbContext();
        var day = await db.Set<Calendar>().IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(c => c.Id == calendarId, CancellationToken);
        return (day.WakeUpTime, day.BedTime, day.HasCustomSleepWindow);
    }

    private async Task<long> SeedActivityAsync(string name)
    {
        await using var db = CreateDbContext();
        var role = new ActivityRole { UserId = UserId, Name = $"{name}-role", Color = "#123456" };
        db.Set<ActivityRole>().Add(role);
        await db.SaveChangesAsync(CancellationToken);

        var activity = new Activity { UserId = UserId, Name = name, RoleId = role.Id };
        db.Set<Activity>().Add(activity);
        await db.SaveChangesAsync(CancellationToken);
        return activity.Id;
    }

    private async Task<long> CreateUserAsync(TimeOnly wake, TimeOnly bed)
    {
        var email = $"sleep-window-{Guid.NewGuid():N}@test.com";
        using var scope = Fixture.UnauthenticatedFactory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();

        var user = new User
        {
            Email = email,
            UserName = email,
            NormalizedEmail = email.ToUpperInvariant(),
            NormalizedUserName = email.ToUpperInvariant(),
            EmailConfirmed = true,
            Locale = AvailableLocales.En,
            Timezone = TimeZoneInfo.Utc,
            DefaultWakeUpTime = wake,
            DefaultBedTime = bed
        };

        (await userManager.CreateAsync(user, Password)).Succeeded.Should().BeTrue();
        return user.Id;
    }

    private record TimeData(int Hours, int Minutes);

    private record UserData(TimeData DefaultWakeUpTime, TimeData DefaultBedTime);
}
