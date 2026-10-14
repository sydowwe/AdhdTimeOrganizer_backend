using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.Core.domain.model.@enum;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sydowwe.Framework.Contracts.notification;
using Sydowwe.Framework.Contracts.notification.payload;
using Sydowwe.Framework.Testing;
using Sydowwe.Notifications.domain.entity;
using Sydowwe.Notifications.infrastructure;
using Sydowwe.Notifications.infrastructure.email;
using Sydowwe.Notifications.infrastructure.webPush;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// P3-01 — quiet hours that follow the sleep schedule, night by night off the calendar.
/// <para>
/// What fails silently here: the switch answers 204 whether or not anything reads the flag, the dispatcher's
/// dependency on the schedule reader is optional, and a reader that fell back to the stored minutes would look
/// right every night the user's days match their default. So the deferral cases give the next day an alarm of its
/// <b>own</b> and assert on the stored <c>DeferredUntil</c>: only a live calendar read lands on it.
/// </para>
/// </summary>
[Collection("Postgres")]
public class QuietHoursFollowSleepScheduleTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;
    private const string FollowRoute = "api/notification-quiet-hours/follow-sleep-schedule";

    private static readonly TimeZoneInfo Bratislava = TimeZoneInfo.FindSystemTimeZoneById("Europe/Bratislava");

    /// <summary>A Tuesday in winter time (UTC+1), far from either DST change.</summary>
    private static readonly DateOnly Tuesday = new(2026, 11, 10);

    /// <summary>Push configured, SMTP off — quiet hours defer only a channel the deployment can deliver on.</summary>
    private static readonly KeyValuePair<string, string?>[] PushOn =
    [
        new($"{EmailNotificationOptions.SectionName}:Enabled", "false"),
        new($"{PushNotificationOptions.SectionName}:VapidSubject", "mailto:test@example.com"),
        new($"{PushNotificationOptions.SectionName}:VapidPublicKey", "test-public-key"),
        new($"{PushNotificationOptions.SectionName}:VapidPrivateKey", "test-private-key")
    ];

    protected override async Task SeedAsync(DbContext db)
    {
        var user = await db.Set<User>().IgnoreQueryFilters().SingleAsync(u => u.Id == UserId);
        user.Timezone = Bratislava;
        await db.SaveChangesAsync();
    }

    // ---- the switch -------------------------------------------------------------------------------

    [Theory(DisplayName = "Following writes the usual night as [bed, wake), overnight and the fallback alike")]
    [InlineData(7, 0, 23, 0, 23 * 60, 7 * 60)]
    [InlineData(8, 0, 0, 0, 0, 8 * 60)]
    public async Task Following_WritesTheDefaultWindowAsBedToWake(
        int wakeHour, int wakeMinute, int bedHour, int bedMinute, int expectedStart, int expectedEnd)
    {
        await SetDefaultWindowAsync(new TimeOnly(wakeHour, wakeMinute), new TimeOnly(bedHour, bedMinute));

        (await FollowAsync()).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await RowAsync()).Should().Be((expectedStart, expectedEnd, true));

        (await GetQuietHoursAsync()).GetProperty("quietHours").GetProperty("followsSleepSchedule").GetBoolean().Should().BeTrue(
            "the card shows the toggle off this field");
    }

    [Fact(DisplayName = "Following replaces a manual window, and setting one by hand again stops the following")]
    public async Task AManualSet_ClearsTheFlag()
    {
        (await SetManualAsync(22 * 60, 6 * 60)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await FollowAsync()).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await RowAsync()).Should().Be((0, 8 * 60, true), "the row now mirrors the (fallback) default window");

        (await SetManualAsync(21 * 60, 5 * 60)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await RowAsync()).Should().Be((21 * 60, 5 * 60, false),
            "a window typed by hand is the user's own — if this stayed true the calendar would keep overriding it");
    }

    [Fact(DisplayName = "A following row shows the usual night live, so a new default reaches the card without a copy")]
    public async Task ChangingTheDefault_ReachesAFollowingRowsReadBack()
    {
        (await FollowAsync()).StatusCode.Should().Be(HttpStatusCode.NoContent);

        await SetDefaultWindowAsync(new TimeOnly(6, 30), new TimeOnly(22, 15));

        var window = (await GetQuietHoursAsync()).GetProperty("quietHours");
        window.GetProperty("startMinute").GetInt32().Should().Be(22 * 60 + 15);
        window.GetProperty("endMinute").GetInt32().Should().Be(6 * 60 + 30,
            "the card must not keep showing the default the user had when they switched");
    }

    [Fact(DisplayName = "The read offers following even before any quiet hours exist")]
    public async Task TheRead_ReportsTheScheduleAvailable_WithNoRow()
    {
        var body = await GetQuietHoursAsync();

        body.GetProperty("quietHours").ValueKind.Should().Be(JsonValueKind.Null);
        body.GetProperty("sleepScheduleAvailable").GetBoolean().Should().BeTrue(
            "the offer matters most to a user with no quiet hours, and the client hides the switch on false");
    }

    [Fact(DisplayName = "A new default leaves a manual quiet-hours window alone")]
    public async Task ChangingTheDefault_LeavesAManualRow()
    {
        (await SetManualAsync(21 * 60, 5 * 60)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        await SetDefaultWindowAsync(new TimeOnly(6, 30), new TimeOnly(22, 15));

        (await RowAsync()).Should().Be((21 * 60, 5 * 60, false));
    }

    // ---- what is enforced -------------------------------------------------------------------------

    /// <summary>
    /// The point of following the calendar rather than the default: Wednesday has an early alarm of its own, so
    /// Tuesday night ends at 05:30, not at the default 07:00 the row's minutes say. A reader that used the minutes
    /// would stamp 06:00Z here instead of 04:30Z.
    /// </summary>
    [Fact(DisplayName = "A push raised during tonight's night is deferred to the next day's own wake-up")]
    public async Task APushInsideTheNight_IsDeferredToTheNextDaysWakeUp()
    {
        await SetDefaultWindowAsync(new TimeOnly(7, 0), new TimeOnly(23, 0));
        await SeedDayAsync(Tuesday, new TimeOnly(7, 0), new TimeOnly(23, 0));
        await SeedDayAsync(Tuesday.AddDays(1), new TimeOnly(5, 30), new TimeOnly(23, 0), custom: true);
        (await FollowAsync()).StatusCode.Should().Be(HttpStatusCode.NoContent);

        // 00:00 Wednesday in Bratislava.
        var deferredUntil = await NotifyAtAsync(new DateTimeOffset(2026, 11, 10, 23, 0, 0, TimeSpan.Zero));

        deferredUntil.Should().Be(new DateTimeOffset(2026, 11, 11, 4, 30, 0, TimeSpan.Zero),
            "05:30 local on Wednesday — that day's alarm, not the 07:00 default");
    }

    [Fact(DisplayName = "A push raised before the day's bed time goes out at once")]
    public async Task APushBeforeBed_IsNotDeferred()
    {
        await SeedDayAsync(Tuesday, new TimeOnly(7, 0), new TimeOnly(23, 30), custom: true);
        await SeedDayAsync(Tuesday.AddDays(1), new TimeOnly(7, 0), new TimeOnly(23, 0));
        (await FollowAsync()).StatusCode.Should().Be(HttpStatusCode.NoContent);

        // 23:10 local: past the 23:00 default, before this day's own 23:30.
        var deferredUntil = await NotifyAtAsync(new DateTimeOffset(2026, 11, 10, 22, 10, 0, TimeSpan.Zero));

        deferredUntil.Should().BeNull("Tuesday's bed time is 23:30; holding this would mean the default was enforced");
    }

    [Fact(DisplayName = "A day whose bed time equals its wake-up time holds nothing that night")]
    public async Task BedEqualToWake_HoldsNothing()
    {
        await SeedDayAsync(Tuesday, new TimeOnly(8, 0), new TimeOnly(8, 0), custom: true);
        await SeedDayAsync(Tuesday.AddDays(1), new TimeOnly(8, 0), new TimeOnly(8, 0), custom: true);
        (await FollowAsync()).StatusCode.Should().Be(HttpStatusCode.NoContent);

        // 03:00 Wednesday local — inside the fallback night the row's minutes describe, outside any night the days do.
        var deferredUntil = await NotifyAtAsync(new DateTimeOffset(2026, 11, 11, 2, 0, 0, TimeSpan.Zero));

        deferredUntil.Should().BeNull("bed time equal to wake-up time means no sleep, so there is nobody to keep quiet for");
    }

    [Fact(DisplayName = "The quiet-hours seam answers a following user from the calendar, not the stored minutes")]
    public async Task TheReader_AnswersAFollowingRowFromTheCalendar()
    {
        await SeedDayAsync(Tuesday, new TimeOnly(7, 0), new TimeOnly(23, 0));
        await SeedDayAsync(Tuesday.AddDays(1), new TimeOnly(5, 30), new TimeOnly(23, 0), custom: true);
        (await FollowAsync()).StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var scope = Fixture.AdminAndUserFactory.Services.CreateAsyncScope();
        var quietUntil = await scope.ServiceProvider.GetRequiredService<IQuietHoursReader>()
            .GetQuietUntilAsync([UserId], new DateTimeOffset(2026, 11, 11, 3, 0, 0, TimeSpan.Zero), CancellationToken);

        // The Reminders scan holds an occurrence on exactly this answer.
        quietUntil.Should().ContainKey(UserId).WhoseValue.Should().Be(new DateTimeOffset(2026, 11, 11, 4, 30, 0, TimeSpan.Zero));
    }

    // ---- helpers ----------------------------------------------------------------------------------

    private Task<HttpResponseMessage> FollowAsync() =>
        CreateClient().PutAsJsonAsync(FollowRoute, new { }, JsonOpts, CancellationToken);

    private Task<JsonElement> GetQuietHoursAsync() =>
        CreateClient().GetFromJsonAsync<JsonElement>("api/notification-quiet-hours", JsonOpts, CancellationToken);

    private Task<HttpResponseMessage> SetManualAsync(int startMinute, int endMinute) =>
        CreateClient().PutAsJsonAsync("api/notification-quiet-hours", new { StartMinute = startMinute, EndMinute = endMinute }, JsonOpts, CancellationToken);

    private async Task SetDefaultWindowAsync(TimeOnly wake, TimeOnly bed)
    {
        var response = await CreateClient().PutAsJsonAsync("api/user/preferences", new
        {
            DefaultWakeUpTime = new { Hours = wake.Hour, Minutes = wake.Minute },
            DefaultBedTime = new { Hours = bed.Hour, Minutes = bed.Minute }
        }, JsonOpts, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync(CancellationToken));
    }

    private async Task<(int Start, int End, bool Follows)> RowAsync()
    {
        await using var db = CreateDbContext();
        var row = await db.Set<NotificationQuietHours>().AsNoTracking().SingleAsync(q => q.UserId == UserId, CancellationToken);
        return (row.StartMinute, row.EndMinute, row.FollowsSleepSchedule);
    }

    /// <summary>Raises one notification to the test user at a fixed instant and returns the row's deferral.</summary>
    private async Task<DateTimeOffset?> NotifyAtAsync(DateTimeOffset at)
    {
        await using var factory = CreateFactory(
            TestRoles.AdminAndUser,
            configureServices: services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(at));
            },
            configOverrides: PushOn);

        await using (var scope = factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<INotificationService>().NotifyAsync(
                NotificationRecipients.User(UserId), new TestNotificationPayload("sleep schedule"), CancellationToken);

        await using var db = CreateDbContext();
        return (await db.Set<Notification>().AsNoTracking().SingleAsync(CancellationToken)).DeferredUntil;
    }

    private async Task SeedDayAsync(DateOnly date, TimeOnly wake, TimeOnly bed, bool custom = false)
    {
        await using var db = CreateDbContext();
        var existing = await db.Set<Calendar>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.UserId == UserId && c.Date == date, CancellationToken);
        if (existing is not null)
            db.Remove(existing);

        db.Set<Calendar>().Add(new Calendar
        {
            UserId = UserId,
            Date = date,
            DayType = DayType.Special,
            WakeUpTime = wake,
            BedTime = bed,
            HasCustomSleepWindow = custom
        });
        await db.SaveChangesAsync(CancellationToken);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
