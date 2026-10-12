using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.Core.domain.model.@enum;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.IntegrationTests.Reminders;
using AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.Planning.application.service.reminder;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.model.entity.reminder;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using AdhdTimeOrganizer.Planning.domain.serviceContract;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sydowwe.Framework.application.dto.dto;
using Sydowwe.Framework.Testing;
using Xunit;
using CultureInfo = System.Globalization.CultureInfo;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// FO4 — "re-plan the rest of today" through its three routes: the preview
/// (<c>POST /calendar/day-plan/{date}/replan</c>), the apply (<c>…/replan/apply</c>) and the single-block read
/// (<c>GET …/next-free-slot</c>).
/// <para>
/// Everything here fails <b>silently</b> if it goes wrong: a mis-packed day is a well-formed 200, a skipped
/// postponement leaves no trace, a half-applied re-plan looks like a day the user edited. So every case asserts on
/// the stored <c>planner_task</c> rows, and the preview is pinned to write none.
/// </para>
/// <para>
/// The host's clock is a replaced <see cref="TimeProvider"/>, so "now" is exact and the day is fixed (2030-03-12);
/// the fixture user is on UTC unless a case moves them. The packing's own edges are pinned without a database in
/// <c>Services.DayPackingTests</c>.
/// </para>
/// </summary>
[Collection("Postgres")]
public class DayReplanTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;
    private static readonly DateOnly Day = new(2030, 3, 12);

    // ---------------------------------------------------------------- the preview

    [Fact(DisplayName = "FO4: the preview packs what slipped around what stays put, keeps lengths and order, and writes nothing")]
    public async Task Preview_PacksAroundFixedBlocks_AndWritesNothing()
    {
        var day = await SeedSlippedMorningAsync();
        var before = await SnapshotAsync();
        await using var host = Host(At(11, 30));

        var (status, body) = await SendAsync(host.CreateClient().PostAsync(Url(Day, "replan"), null, CancellationToken));
        status.Should().Be(HttpStatusCode.OK, body);
        var preview = Read<DayReplanPreviewResponse>(body);

        // In-progress D holds to 11:45, unavoidable C 12:15–13:00, completed G 13:00–13:30. A (an hour) fits in
        // neither 11:30 nor the 11:45–12:15 gap, so it goes after G; H (20 min) back-fills the gap; E, upcoming,
        // keeps its turn but is pushed past what the slipped blocks took. Background F and skipped X hold nothing.
        preview.Moves.Select(m => (m.Task.Id, Slot(m.StartTime, m.EndTime), m.State)).Should().Equal(
            (day.H, "11:45-12:05", PlannerTaskSlipState.Late),
            (day.A, "13:30-14:30", PlannerTaskSlipState.Missed),
            (day.B, "14:30-15:15", PlannerTaskSlipState.Late),
            (day.E, "15:15-16:15", PlannerTaskSlipState.Upcoming));
        preview.WontFit.Should().BeEmpty();
        preview.HasChanges.Should().BeTrue();
        Slot(preview.PackedFrom, preview.WindowEnd).Should().Be("11:30-23:59", "bed time 00:00 means the end of the day");
        preview.Moves.Single(m => m.Task.Id == day.A).Task.StartTime.Should().Be(new TimeDto(10, 0),
            "each move carries the task as it stands, so the client can draw from-and-to");

        // The wire contract the client codes against: property names and enum spellings.
        using (var json = JsonDocument.Parse(body))
        {
            var root = json.RootElement;
            root.GetProperty("previewToken").GetString().Should().NotBeNullOrEmpty();
            root.GetProperty("hasChanges").GetBoolean().Should().BeTrue();
            root.GetProperty("packedFrom").GetProperty("hours").GetInt32().Should().Be(11);
            var first = root.GetProperty("moves")[0];
            first.GetProperty("state").GetString().Should().Be("Late");
            first.GetProperty("startTime").GetProperty("minutes").GetInt32().Should().Be(45);
            first.GetProperty("task").GetProperty("id").GetInt64().Should().Be(day.H);
            root.GetProperty("wontFit").GetArrayLength().Should().Be(0);
        }

        (await SnapshotAsync()).Should().Equal(before, "a preview never writes a planner task");
    }

    [Fact(DisplayName = "FO4: with nothing slipped the preview is an empty 200 — overlapping upcoming blocks are left alone")]
    public async Task NothingSlipped_IsAnEmptyPreview()
    {
        await using (var db = CreateDbContext())
        {
            var calendar = await DayAsync(db, Day);
            var work = await ActivityAsync(db);
            var unavoidable = await ActivityAsync(db, unavoidable: true);
            await TaskAsync(db, calendar, work, "10:00", "11:00");
            await TaskAsync(db, calendar, work, "10:30", "11:30");
            await TaskAsync(db, calendar, work, "07:00", "08:00", PlannerTaskStatus.Completed);
            await TaskAsync(db, calendar, work, "08:30", "09:30", PlannerTaskStatus.InProgress);
            // Missed, but unavoidable: it does not move, so it is not a reason to re-plan.
            await TaskAsync(db, calendar, unavoidable, "07:30", "08:30");
        }
        var before = await SnapshotAsync();
        await using var host = Host(At(9, 0));
        var client = host.CreateClient();

        var preview = await PreviewAsync(client, Day);

        preview.Moves.Should().BeEmpty();
        preview.WontFit.Should().BeEmpty();
        preview.HasChanges.Should().BeFalse();

        var (status, body) = await SendAsync(ApplyAsync(client, Day, preview.PreviewToken));
        status.Should().Be(HttpStatusCode.OK, body);
        var applied = Read<ApplyDayReplanResponse>(body);
        applied.MovedTaskIds.Should().BeEmpty();
        (await SnapshotAsync()).Should().Equal(before);
    }

    [Fact(DisplayName = "FO10: on a day with no row the preview reports the user's own bed time, not the generic one")]
    public async Task Preview_DayWithNoRow_ReportsTheUsersBedTime()
    {
        await SetDefaultBedTimeAsync(new TimeOnly(22, 0));
        try
        {
            await using var host = Host(At(11, 30));

            var preview = await PreviewAsync(host.CreateClient(), Day);

            preview.Moves.Should().BeEmpty("a day with no row has no tasks");
            Slot(preview.PackedFrom, preview.WindowEnd).Should().Be("11:30-22:00",
                "the same window next-free-slot packs into for such a day");
        }
        finally
        {
            await SetDefaultBedTimeAsync(UserSleepWindow.Fallback.BedTime);
        }
    }

    private async Task SetDefaultBedTimeAsync(TimeOnly bedTime)
    {
        await using var db = CreateDbContext();
        var user = await db.Set<User>().IgnoreQueryFilters().SingleAsync(u => u.Id == UserId, CancellationToken);
        user.DefaultBedTime = bedTime;
        await db.SaveChangesAsync(CancellationToken);
    }

    [Fact(DisplayName = "FO4: when time runs out optional blocks give way first, and what still has no room comes back")]
    public async Task OptionalGivesWayFirst()
    {
        long optional, first, second;
        await using (var db = CreateDbContext())
        {
            var calendar = await DayAsync(db, Day);
            var work = await ActivityAsync(db);
            var optionalLevel = await PlanningTestSeedHelper.SeedTaskImportanceAsync(
                db, TaskImportance.OptionalMarkerValue, UserId, ct: CancellationToken);
            optional = await TaskAsync(db, calendar, work, "18:00", "19:00", importanceId: optionalLevel);
            first = await TaskAsync(db, calendar, work, "19:00", "20:00");
            second = await TaskAsync(db, calendar, work, "20:00", "20:45");
        }
        await using var host = Host(At(22, 0));

        var preview = await PreviewAsync(host.CreateClient(), Day);

        // 22:00–23:59: in order the optional hour would leave the second required block without room.
        preview.Moves.Select(m => (m.Task.Id, Slot(m.StartTime, m.EndTime))).Should().Equal(
            (first, "22:00-23:00"),
            (second, "23:00-23:45"));
        preview.WontFit.Should().ContainSingle()
            .Which.Should().Match<DayReplanWontFitResponse>(w => w.Task.Id == optional && w.IsOptional && w.State == PlannerTaskSlipState.Missed);
    }

    [Fact(DisplayName = "FO4: a block longer than the time left does not fit while a shorter one still moves; bed time ends the window")]
    public async Task LongerThanTheTimeLeft_WontFit()
    {
        var (longOne, shortOne) = await SeedTooLongAsync();
        await using var host = Host(At(11, 30));

        var preview = await PreviewAsync(host.CreateClient(), Day);

        Slot(preview.PackedFrom, preview.WindowEnd).Should().Be("11:30-13:00");
        preview.Moves.Select(m => (m.Task.Id, Slot(m.StartTime, m.EndTime))).Should().Equal((shortOne, "11:30-12:00"));
        preview.WontFit.Select(w => (w.Task.Id, w.IsOptional)).Should().Equal((longOne, false));
    }

    // ---------------------------------------------------------------- the apply

    [Fact(DisplayName = "FO4: applying moves every block in one go, counts slips exactly as a manual move would, and re-syncs reminders once")]
    public async Task Apply_MovesEverything_CountsSlips_AndSyncsRemindersOnce()
    {
        var day = await SeedSlippedMorningAsync();
        long reminderOnA;
        await using (var db = CreateDbContext())
            reminderOnA = (await ReminderSeedHelper.SeedReminderAsync(
                db, UserId, Day.ToDateTime(T("10:00")), plannerTaskId: day.A, ct: CancellationToken)).Id;

        var log = new SyncLog();
        await using var host = Host(At(11, 30), log);
        var client = host.CreateClient();
        var preview = await PreviewAsync(client, Day);

        var (status, body) = await SendAsync(ApplyAsync(client, Day, preview.PreviewToken));

        status.Should().Be(HttpStatusCode.OK, body);
        var applied = Read<ApplyDayReplanResponse>(body);
        applied.MovedTaskIds.Should().BeEquivalentTo([day.H, day.A, day.B, day.E]);
        applied.MovedToTomorrowTaskIds.Should().BeEmpty();
        applied.SkippedTaskIds.Should().BeEmpty();

        var a = await LoadAsync(day.A);
        Slot(a).Should().Be("13:30-14:30");
        a.PostponeCount.Should().Be(1, "a missed block pushed later is a postponement, as the PUT would count it");
        a.OriginalDate.Should().Be(Day);
        a.OriginalStartTime.Should().Be(T("10:00"));
        a.CarryOverCount.Should().Be(0);

        (await LoadAsync(day.B)).PostponeCount.Should().Be(1, "its 11:00 start had passed");
        (await LoadAsync(day.H)).PostponeCount.Should().Be(1);

        var e = await LoadAsync(day.E);
        Slot(e).Should().Be("15:15-16:15");
        e.PostponeCount.Should().Be(0, "pushed before its own 14:00 came — a re-plan, not a slip");
        e.OriginalDate.Should().BeNull();

        foreach (var (id, slot) in new[]
                 {
                     (day.D, "11:15-11:45"), (day.C, "12:15-13:00"), (day.G, "13:00-13:30"),
                     (day.F, "09:00-17:00"), (day.X, "10:30-11:00")
                 })
            Slot(await LoadAsync(id)).Should().Be(slot, "fixed, background and skipped blocks stay put");

        log.Calls.Should().ContainSingle("one reminder sync for the whole set")
            .Which.Should().BeEquivalentTo([day.H, day.A, day.B, day.E]);

        await using var check = CreateDbContext();
        (await check.Set<Reminder>().SingleAsync(r => r.Id == reminderOnA, CancellationToken)).RemindAt
            .Should().Be(Day.ToDateTime(T("13:30")), "a task-linked reminder follows its task");
    }

    [Fact(DisplayName = "FO4: with no time left everything is a won't-fit, optional first; the apply moves some to tomorrow and skips the rest")]
    public async Task OutOfTime_ToTomorrowAndSkip()
    {
        long first, optional, second, reminderOnFirst;
        await using (var db = CreateDbContext())
        {
            var calendar = await DayAsync(db, Day);
            var work = await ActivityAsync(db);
            var optionalLevel = await PlanningTestSeedHelper.SeedTaskImportanceAsync(
                db, TaskImportance.OptionalMarkerValue, UserId, ct: CancellationToken);
            first = await TaskAsync(db, calendar, work, "18:00", "19:00");
            optional = await TaskAsync(db, calendar, work, "19:00", "19:30", importanceId: optionalLevel);
            second = await TaskAsync(db, calendar, work, "20:00", "20:45");
            reminderOnFirst = (await ReminderSeedHelper.SeedReminderAsync(
                db, UserId, Day.ToDateTime(T("18:00")), plannerTaskId: first, ct: CancellationToken)).Id;
        }

        var log = new SyncLog();
        await using var host = Host(At(23, 45), log);
        var client = host.CreateClient();
        var preview = await PreviewAsync(client, Day);

        preview.Moves.Should().BeEmpty("23:45–23:59 holds none of them");
        preview.WontFit.Select(w => (w.Task.Id, w.IsOptional)).Should().Equal(
            (optional, true), (first, false), (second, false));

        var (status, body) = await SendAsync(ApplyAsync(client, Day, preview.PreviewToken,
            (optional, "Skip"), (first, "Tomorrow"), (second, "Tomorrow")));

        status.Should().Be(HttpStatusCode.OK, body);
        var applied = Read<ApplyDayReplanResponse>(body);
        applied.MovedToTomorrowTaskIds.Should().BeEquivalentTo([first, second]);
        applied.SkippedTaskIds.Should().Equal(optional);
        applied.MovedTaskIds.Should().BeEmpty();

        var skipped = await LoadAsync(optional);
        skipped.Status.Should().Be(PlannerTaskStatus.Cancelled);
        skipped.SkipReasonKey.Should().Be(SkipReasonKey.NoTime);
        skipped.PostponeCount.Should().Be(0);

        foreach (var (id, start, slot) in new[] { (first, "18:00", "18:00-19:00"), (second, "20:00", "20:00-20:45") })
        {
            var moved = await LoadAsync(id);
            moved.Calendar.Date.Should().Be(Day.AddDays(1));
            Slot(moved).Should().Be(slot, "a block moved to tomorrow keeps its time of day");
            moved.Status.Should().Be(PlannerTaskStatus.NotStarted);
            moved.PostponeCount.Should().Be(1, "a move to tomorrow is a postponement, as a manual move would count it");
            moved.CarryOverCount.Should().Be(0, "only the carry-over command counts carry-overs");
            moved.OriginalDate.Should().Be(Day);
            moved.OriginalStartTime.Should().Be(T(start));
        }

        log.Calls.Should().ContainSingle().Which.Should().BeEquivalentTo([first, second, optional]);
        await using var check = CreateDbContext();
        (await check.Set<Calendar>().CountAsync(c => c.UserId == UserId && c.Date == Day.AddDays(1), CancellationToken))
            .Should().Be(1, "tomorrow was created for the moved blocks");
        (await check.Set<Reminder>().SingleAsync(r => r.Id == reminderOnFirst, CancellationToken)).RemindAt
            .Should().Be(Day.AddDays(1).ToDateTime(T("18:00")));
    }

    [Fact(DisplayName = "FO4: the user's zone decides today and the clock — across the UTC date line")]
    public async Task UsersZone_AcrossTheUtcDateLine()
    {
        // 22:30 UTC on the 12th is 10:30 on the 13th at UTC+12.
        var local = Day.AddDays(1);
        long missed;
        await using (var db = CreateDbContext())
        {
            var user = await db.Set<User>().SingleAsync(u => u.Id == UserId, CancellationToken);
            user.Timezone = TimeZoneInfo.FindSystemTimeZoneById("Etc/GMT-12");
            await db.SaveChangesAsync(CancellationToken);

            var calendar = await DayAsync(db, local);
            missed = await TaskAsync(db, calendar, await ActivityAsync(db), "09:00", "10:00");
        }
        await using var host = Host(At(22, 30));
        var client = host.CreateClient();

        var (utcStatus, utcBody) = await SendAsync(client.PostAsync(Url(Day, "replan"), null, CancellationToken));
        utcStatus.Should().Be(HttpStatusCode.BadRequest, "the 12th is UTC's today, not the user's: " + utcBody);

        var preview = await PreviewAsync(client, local);
        preview.Moves.Select(m => (m.Task.Id, Slot(m.StartTime, m.EndTime), m.State))
            .Should().Equal((missed, "10:30-11:30", PlannerTaskSlipState.Missed));

        var (status, body) = await SendAsync(ApplyAsync(client, local, preview.PreviewToken));
        status.Should().Be(HttpStatusCode.OK, body);

        // In UTC the 13th's 09:00 has not come yet; only the user's clock makes this a slip.
        var task = await LoadAsync(missed);
        Slot(task).Should().Be("10:30-11:30");
        task.PostponeCount.Should().Be(1);
        task.OriginalDate.Should().Be(local);
    }

    [Fact(DisplayName = "FO4: only the user's today can be re-planned; a past day has no free slot; malformed input is a 400")]
    public async Task WrongDatesAndMalformedInput_Are400()
    {
        await SeedSlippedMorningAsync();
        var before = await SnapshotAsync();
        await using var host = Host(At(11, 30));
        var client = host.CreateClient();

        foreach (var date in new[] { Day.AddDays(-1), Day.AddDays(1) })
        {
            (await SendAsync(client.PostAsync(Url(date, "replan"), null, CancellationToken))).Status
                .Should().Be(HttpStatusCode.BadRequest, $"{date} is not today");
            (await SendAsync(ApplyAsync(client, date, "690.0123456789abcdef01234567"))).Status
                .Should().Be(HttpStatusCode.BadRequest, $"{date} is not today");
        }

        (await SendAsync(client.PostAsync("api/calendar/day-plan/not-a-date/replan", null, CancellationToken))).Status
            .Should().Be(HttpStatusCode.BadRequest);
        (await SendAsync(ApplyAsync(client, Day, "not-a-token"))).Status.Should().Be(HttpStatusCode.BadRequest);
        (await SendAsync(client.GetAsync(Url(Day.AddDays(-1), "next-free-slot?minutes=30"), CancellationToken))).Status
            .Should().Be(HttpStatusCode.BadRequest);
        (await SendAsync(client.GetAsync(Url(Day, "next-free-slot?minutes=0"), CancellationToken))).Status
            .Should().Be(HttpStatusCode.BadRequest);

        (await SnapshotAsync()).Should().Equal(before);
    }

    [Fact(DisplayName = "FO4: a preview of a day that has changed since is a 409, and nothing is written")]
    public async Task StalePreview_Is409_AndWritesNothing()
    {
        var day = await SeedSlippedMorningAsync();
        await using var host = Host(At(11, 30));
        var client = host.CreateClient();
        var preview = await PreviewAsync(client, Day);

        // Another device starts something in the gap the preview was going to fill.
        await using (var db = CreateDbContext())
            await TaskAsync(db, day.Calendar, await ActivityAsync(db), "11:45", "12:15", PlannerTaskStatus.InProgress);
        var before = await SnapshotAsync();

        var (status, body) = await SendAsync(ApplyAsync(client, Day, preview.PreviewToken));

        status.Should().Be(HttpStatusCode.Conflict, body);
        ErrorType(body).Should().Be("Conflict");
        (await SnapshotAsync()).Should().Equal(before, "a stale preview never half-applies");
    }

    [Fact(DisplayName = "FO4: a preview older than ten minutes is a 409; one within them applies")]
    public async Task ExpiredPreview_Is409()
    {
        var day = await SeedSlippedMorningAsync();
        var clock = At(11, 30);
        await using var host = Host(clock);
        var client = host.CreateClient();

        var old = await PreviewAsync(client, Day);
        clock.Now = clock.Now.AddMinutes(11);
        var before = await SnapshotAsync();

        var (status, body) = await SendAsync(ApplyAsync(client, Day, old.PreviewToken));
        status.Should().Be(HttpStatusCode.Conflict, body);
        ErrorType(body).Should().Be("Conflict");
        (await SnapshotAsync()).Should().Equal(before);

        // The control: a lifetime that rejected everything would pass the half above.
        var fresh = await PreviewAsync(client, Day);
        clock.Now = clock.Now.AddMinutes(9);
        var (freshStatus, freshBody) = await SendAsync(ApplyAsync(client, Day, fresh.PreviewToken));
        freshStatus.Should().Be(HttpStatusCode.OK, freshBody);
        (await LoadAsync(day.A)).PostponeCount.Should().Be(1);
    }

    [Fact(DisplayName = "FO4: a double-submitted apply is refused the second time and counts the slip once")]
    public async Task DoubleSubmit_IsAppliedOnce()
    {
        var day = await SeedSlippedMorningAsync();
        await using var host = Host(At(11, 30));
        var client = host.CreateClient();
        var preview = await PreviewAsync(client, Day);

        var (firstStatus, firstBody) = await SendAsync(ApplyAsync(client, Day, preview.PreviewToken));
        firstStatus.Should().Be(HttpStatusCode.OK, firstBody);
        var afterFirst = await SnapshotAsync();

        var (secondStatus, secondBody) = await SendAsync(ApplyAsync(client, Day, preview.PreviewToken));

        secondStatus.Should().Be(HttpStatusCode.Conflict, secondBody);
        ErrorType(secondBody).Should().Be("Conflict");
        (await SnapshotAsync()).Should().Equal(afterFirst);
        var a = await LoadAsync(day.A);
        Slot(a).Should().Be("13:30-14:30");
        a.PostponeCount.Should().Be(1);
    }

    [Fact(DisplayName = "FO4: the apply must decide exactly the preview's won't-fit blocks")]
    public async Task WontFitDecisions_MustMatchThePreview()
    {
        var (longOne, shortOne) = await SeedTooLongAsync();
        await using var host = Host(At(11, 30));
        var client = host.CreateClient();
        var preview = await PreviewAsync(client, Day);
        var before = await SnapshotAsync();

        (await SendAsync(ApplyAsync(client, Day, preview.PreviewToken))).Status
            .Should().Be(HttpStatusCode.BadRequest, "the long block is left undecided");
        (await SendAsync(ApplyAsync(client, Day, preview.PreviewToken, (longOne, "Skip"), (shortOne, "Skip")))).Status
            .Should().Be(HttpStatusCode.BadRequest, "the short block fits, so it is not the caller's to skip");
        (await SendAsync(ApplyAsync(client, Day, preview.PreviewToken, (longOne, "Skip"), (longOne, "Tomorrow")))).Status
            .Should().Be(HttpStatusCode.BadRequest, "one decision per block");
        (await SnapshotAsync()).Should().Equal(before);

        var (status, body) = await SendAsync(ApplyAsync(client, Day, preview.PreviewToken, (longOne, "Tomorrow")));
        status.Should().Be(HttpStatusCode.OK, body);
        (await LoadAsync(longOne)).Calendar.Date.Should().Be(Day.AddDays(1));
        Slot(await LoadAsync(shortOne)).Should().Be("11:30-12:00");
    }

    // ---------------------------------------------------------------- next free slot

    [Fact(DisplayName = "FO4: the next free slot is the earliest stretch of the day long enough, by the same rule")]
    public async Task NextFreeSlot()
    {
        long upcoming;
        await using (var db = CreateDbContext())
        {
            var calendar = await DayAsync(db, Day);
            var work = await ActivityAsync(db);
            await TaskAsync(db, calendar, work, "11:15", "11:45", PlannerTaskStatus.InProgress);
            upcoming = await TaskAsync(db, calendar, work, "12:00", "13:00");
            await TaskAsync(db, calendar, work, "09:00", "17:00", background: true);
            await TaskAsync(db, calendar, work, "11:45", "12:00", PlannerTaskStatus.Cancelled);
        }
        var before = await SnapshotAsync();
        await using var host = Host(At(11, 30));
        var client = host.CreateClient();

        (await SlotAsync(client, Day, "minutes=15")).Should().Be("11:45-12:00", "background and skipped blocks hold no time");
        (await SlotAsync(client, Day, "minutes=30")).Should().Be("13:00-13:30", "the upcoming block holds 12:00–13:00");
        (await SlotAsync(client, Day, $"minutes=60&excludeTaskId={upcoming}")).Should().Be("11:45-12:45",
            "the block being moved does not stand in its own way");
        (await SlotAsync(client, Day, "minutes=30&notBeforeMinute=840")).Should().Be("14:00-14:30");
        (await SlotAsync(client, Day.AddDays(2), "minutes=30")).Should().Be("08:00-08:30",
            "a later day with no row starts at the default wake-up time");

        var (status, body) = await SendAsync(client.GetAsync(Url(Day, "next-free-slot?minutes=700"), CancellationToken));
        status.Should().Be(HttpStatusCode.OK, body);
        var none = Read<NextFreeSlotResponse>(body);
        none.Found.Should().BeFalse("13:00–23:59 is 659 minutes");
        none.StartTime.Should().BeNull();
        none.Minutes.Should().Be(700);

        (await SnapshotAsync()).Should().Equal(before);
    }

    // ---------------------------------------------------------------- fixtures

    private sealed record SlippedMorning(long Calendar, long A, long B, long H, long D, long C, long G, long E, long F, long X);

    /// <summary>
    /// The FO4 moment at 11:30: A (10:00–11:00) missed; B (11:00–11:45) and H (11:20–11:40) late, both overlapping
    /// in-progress D (11:15–11:45); C (12:15–13:00) of an unavoidable activity; G (13:00–13:30) completed; E
    /// (14:00–15:00) upcoming; F a background block over the whole day; X skipped.
    /// </summary>
    private async Task<SlippedMorning> SeedSlippedMorningAsync()
    {
        await using var db = CreateDbContext();
        var calendar = await DayAsync(db, Day);
        var work = await ActivityAsync(db);
        var unavoidable = await ActivityAsync(db, unavoidable: true);
        return new SlippedMorning(
            calendar,
            A: await TaskAsync(db, calendar, work, "10:00", "11:00"),
            B: await TaskAsync(db, calendar, work, "11:00", "11:45"),
            H: await TaskAsync(db, calendar, work, "11:20", "11:40"),
            D: await TaskAsync(db, calendar, work, "11:15", "11:45", PlannerTaskStatus.InProgress),
            C: await TaskAsync(db, calendar, unavoidable, "12:15", "13:00"),
            G: await TaskAsync(db, calendar, work, "13:00", "13:30", PlannerTaskStatus.Completed),
            E: await TaskAsync(db, calendar, work, "14:00", "15:00"),
            F: await TaskAsync(db, calendar, work, "09:00", "17:00", background: true),
            X: await TaskAsync(db, calendar, work, "10:30", "11:00", PlannerTaskStatus.Cancelled));
    }

    /// <summary>Bed time 13:00, so at 11:30 ninety minutes are left: a two-hour missed block and a half-hour one.</summary>
    private async Task<(long Long, long Short)> SeedTooLongAsync()
    {
        await using var db = CreateDbContext();
        var calendar = await DayAsync(db, Day, bedTime: T("13:00"));
        var work = await ActivityAsync(db);
        return (await TaskAsync(db, calendar, work, "08:00", "10:00"), await TaskAsync(db, calendar, work, "10:00", "10:30"));
    }

    private async Task<long> DayAsync(DbContext db, DateOnly date, TimeOnly? bedTime = null)
    {
        var calendar = new Calendar
        {
            UserId = UserId,
            Date = date,
            DayType = DayType.Workday,
            WakeUpTime = T("07:00"),
            BedTime = bedTime ?? T("00:00")
        };
        db.Set<Calendar>().Add(calendar);
        await db.SaveChangesAsync(CancellationToken);
        return calendar.Id;
    }

    private async Task<long> ActivityAsync(DbContext db, bool unavoidable = false)
    {
        var role = new ActivityRole { UserId = UserId, Name = $"replan-role-{Guid.NewGuid():N}", Color = "#123456" };
        db.Set<ActivityRole>().Add(role);
        await db.SaveChangesAsync(CancellationToken);

        var activity = new Activity { UserId = UserId, Name = $"replan-{Guid.NewGuid():N}", RoleId = role.Id, IsUnavoidable = unavoidable };
        db.Set<Activity>().Add(activity);
        await db.SaveChangesAsync(CancellationToken);
        return activity.Id;
    }

    private async Task<long> TaskAsync(
        DbContext db, long calendarId, long activityId, string start, string end,
        PlannerTaskStatus status = PlannerTaskStatus.NotStarted, bool background = false, long? importanceId = null)
    {
        var task = new PlannerTask
        {
            UserId = UserId,
            ActivityId = activityId,
            CalendarId = calendarId,
            StartTime = T(start),
            EndTime = T(end),
            IsBackground = background,
            Status = status,
            ImportanceId = importanceId
        };
        db.Set<PlannerTask>().Add(task);
        await db.SaveChangesAsync(CancellationToken);
        return task.Id;
    }

    private async Task<PlannerTask> LoadAsync(long taskId)
    {
        await using var db = CreateDbContext();
        return await db.Set<PlannerTask>().AsNoTracking().Include(t => t.Calendar)
            .SingleAsync(t => t.Id == taskId, CancellationToken);
    }

    /// <summary>Every planner task's slot, day, status and slip record — what "nothing was written" compares.</summary>
    private async Task<List<string>> SnapshotAsync()
    {
        await using var db = CreateDbContext();
        var rows = await db.Set<PlannerTask>().AsNoTracking().OrderBy(t => t.Id)
            .Select(t => new
            {
                t.Id, t.CalendarId, t.StartTime, t.EndTime, t.Status, t.SkipReasonKey,
                t.PostponeCount, t.OriginalDate, t.OriginalStartTime
            })
            .ToListAsync(CancellationToken);
        return rows.Select(r => r.ToString()!).ToList();
    }

    // ---------------------------------------------------------------- host and HTTP

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static Clock At(int hour, int minute) =>
        new(new DateTimeOffset(Day.ToDateTime(new TimeOnly(hour, minute)), TimeSpan.Zero));

    private sealed class SyncLog
    {
        public List<long[]> Calls { get; } = [];
    }

    /// <summary>The real registration service, recording each <c>SyncForPlannerTasksAsync</c> call.</summary>
    private sealed class LoggingReminders(IReminderRegistrationService inner, SyncLog log) : IReminderRegistrationService
    {
        public Task SyncAsync(Reminder reminder, CancellationToken ct = default) => inner.SyncAsync(reminder, ct);

        public Task ApplyUserDefaultsAsync(Reminder reminder, bool leadOffsetsWereSpecified, CancellationToken ct = default) =>
            inner.ApplyUserDefaultsAsync(reminder, leadOffsetsWereSpecified, ct);

        public Task CancelAsync(long reminderId, CancellationToken ct = default) => inner.CancelAsync(reminderId, ct);

        public Task CancelManyAsync(IReadOnlyCollection<long> reminderIds, CancellationToken ct = default) =>
            inner.CancelManyAsync(reminderIds, ct);

        public Task SyncForPlannerTasksAsync(IReadOnlyCollection<long> plannerTaskIds, CancellationToken ct = default)
        {
            lock (log)
                log.Calls.Add(plannerTaskIds.ToArray());
            return inner.SyncForPlannerTasksAsync(plannerTaskIds, ct);
        }

        public Task<IReadOnlyList<long>> GetReminderIdsForPlannerTasksAsync(IReadOnlyCollection<long> plannerTaskIds, CancellationToken ct = default) =>
            inner.GetReminderIdsForPlannerTasksAsync(plannerTaskIds, ct);
    }

    private ITestClientFactory Host(Clock clock, SyncLog? log = null) =>
        CreateFactory(TestRoles.AdminAndUser, configureServices: services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
            if (log is null)
                return;

            services.RemoveAll<IReminderRegistrationService>();
            services.AddScoped<ReminderRegistrationService>();
            services.AddScoped<IReminderRegistrationService>(sp =>
                new LoggingReminders(sp.GetRequiredService<ReminderRegistrationService>(), log));
        });

    private static string Url(DateOnly date, string suffix) =>
        $"api/calendar/day-plan/{date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}/{suffix}";

    private static async Task<(HttpStatusCode Status, string Body)> SendAsync(Task<HttpResponseMessage> request)
    {
        using var response = await request;
        return (response.StatusCode, await response.Content.ReadAsStringAsync(CancellationToken));
    }

    private static async Task<DayReplanPreviewResponse> PreviewAsync(HttpClient client, DateOnly date)
    {
        var (status, body) = await SendAsync(client.PostAsync(Url(date, "replan"), null, CancellationToken));
        status.Should().Be(HttpStatusCode.OK, body);
        return Read<DayReplanPreviewResponse>(body);
    }

    private static Task<HttpResponseMessage> ApplyAsync(
        HttpClient client, DateOnly date, string token, params (long TaskId, string Resolution)[] wontFit) =>
        client.PostAsJsonAsync(Url(date, "replan/apply"), new
        {
            PreviewToken = token,
            WontFit = wontFit.Select(w => new { w.TaskId, w.Resolution }).ToArray()
        }, JsonOpts, CancellationToken);

    private static async Task<string> SlotAsync(HttpClient client, DateOnly date, string query)
    {
        var (status, body) = await SendAsync(client.GetAsync(Url(date, $"next-free-slot?{query}"), CancellationToken));
        status.Should().Be(HttpStatusCode.OK, body);
        var slot = Read<NextFreeSlotResponse>(body);
        slot.Found.Should().BeTrue(body);
        return Slot(slot.StartTime!, slot.EndTime!);
    }

    private static T Read<T>(string body) => JsonSerializer.Deserialize<T>(body, JsonOpts)!;

    private static string? ErrorType(string body)
    {
        using var json = JsonDocument.Parse(body);
        return json.RootElement.TryGetProperty("errorType", out var type) ? type.GetString() : null;
    }

    private static TimeOnly T(string hhmm) => TimeOnly.ParseExact(hhmm, "HH:mm", CultureInfo.InvariantCulture);

    private static string Hm(int hours, int minutes) => $"{hours:00}:{minutes:00}";

    private static string Slot(TimeDto start, TimeDto end) => $"{Hm(start.Hours, start.Minutes)}-{Hm(end.Hours, end.Minutes)}";

    private static string Slot(PlannerTask task) =>
        $"{Hm(task.StartTime.Hour, task.StartTime.Minute)}-{Hm(task.EndTime.Hour, task.EndTime.Minute)}";
}
