using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Planning.application.dto.response.reminder;
using AdhdTimeOrganizer.Planning.application.service.reminder;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using AdhdTimeOrganizer.Planning.domain.model.entity.reminder;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Sydowwe.Notifications.infrastructure.email;
using Sydowwe.Reminders.domain.entity;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Reminders;

/// <summary>
/// Shared rigging for TEST-10. Everything below runs with the test user in <b>Europe/Bratislava</b> rather
/// than UTC, which is the whole point: with a UTC user every one of these assertions passes against a day
/// boundary computed straight off the stored instant, and the arithmetic under test never runs.
/// </summary>
public abstract class ReminderDayViewTestBase(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    protected const long UserId = FakeLoggedUserService.TestUserId;

    /// <summary>UTC+1 in winter, UTC+2 in summer, and it observes both DST transitions Part 2 is about.</summary>
    protected static readonly TimeZoneInfo Bratislava = TimeZoneInfo.FindSystemTimeZoneById("Europe/Bratislava");

    /// <summary>
    /// The fixture sets every MAIL_* var, so SMTP auto-detects as configured. Nothing here runs the scan, but
    /// the reminder-creating tests borrow <see cref="ReminderRegistrationTests"/>'s master switch anyway so a
    /// future dispatch-on-register can never turn these into network calls.
    /// </summary>
    protected static readonly KeyValuePair<string, string?>[] EmailOff =
        [new($"{EmailNotificationOptions.SectionName}:Enabled", "false")];

    protected override async Task SeedAsync(DbContext db)
    {
        var user = await db.Set<User>().IgnoreQueryFilters().SingleAsync(u => u.Id == UserId);
        user.Timezone = Bratislava;
        await db.SaveChangesAsync();
    }

    /// <summary>A wall-clock reading in the user's own zone, as the absolute instant the database stores.</summary>
    protected static DateTime Instant(DateOnly localDate, TimeOnly localTime) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localDate.ToDateTime(localTime), DateTimeKind.Unspecified), Bratislava);

    protected static DateOnly Date(string yyyyMMdd) => DateOnly.ParseExact(yyyyMMdd, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>
    /// A <c>DateTime</c> off the wire or out of Npgsql, normalised so the comparison cannot be decided by
    /// <see cref="DateTimeKind"/> rather than by the instant — which is exactly the confusion under test.
    /// </summary>
    protected static DateTime AsUtc(DateTime value) => value.Kind == DateTimeKind.Unspecified
        ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
        : value.ToUniversalTime();

    protected async Task<List<ReminderOnDateResponse>> DayViewAsync(DateOnly date)
    {
        var response = await CreateUserRoleClient().GetAsync($"api/reminder/by-date/{date:yyyy-MM-dd}", CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<List<ReminderOnDateResponse>>(JsonOpts, CancellationToken))!;
    }
}

// =====================================================================================================
// Part 1 — GetByDateReminderEndpoint.OccursOn.
//
// GET api/reminder/by-date/{date} takes the caller's LOCAL date. One-shots are filtered in SQL by the
// local-day instant range; recurring ones are filtered to "anchored before this day ends" and then
// expanded in memory. Both halves are asserted on OccursAt as well as RemindAt — an implementation that
// returned the anchor for OccursAt would satisfy every id-only assertion and still put a birthday nudge
// thirty years in the past on the user's screen.
// =====================================================================================================

[Collection("Postgres")]
public class ReminderDayViewTests(AppDbContextFixture fixture) : ReminderDayViewTestBase(fixture)
{
    /// <summary>Every recurrence case anchors here, so a wrong <c>OccursAt</c> cannot hide behind midnight.</summary>
    private static readonly TimeOnly AnchorTime = new(8, 0);

    /// <summary>
    /// The cadence table. Anchor and query date are both <b>local</b> dates; the anchor time is 08:00 local in
    /// every row, so the expected <c>OccursAt</c> is always "08:00 local on the requested day" — a different
    /// UTC instant from the anchor's whenever the two dates sit on opposite sides of a DST switch, which
    /// several of these deliberately do.
    /// </summary>
    public static TheoryData<ReminderRecurrence, string, string, bool, string> Cadences => new()
    {
        { ReminderRecurrence.Daily, "2027-03-10", "2027-06-15", true, "a daily reminder lands on every day at or after its anchor" },
        // Absent because the endpoint never loads it: the SQL half already filters to RemindAt < dayEnd, so
        // OccursOn's own `date < anchorDate` guard is unreachable over HTTP in a UTC+n zone. Both mechanisms
        // say the same thing — a reminder that has not started yet is not on an earlier day.
        { ReminderRecurrence.Daily, "2027-06-16", "2027-06-15", false, "a reminder anchored tomorrow is not on today's list" },
        { ReminderRecurrence.Weekly, "2027-06-01", "2027-06-08", true, "2027-06-01 and 2027-06-08 are both Tuesdays" },
        { ReminderRecurrence.Weekly, "2027-06-01", "2027-06-09", false, "the Wednesday after a Tuesday anchor is not an occurrence" },
        { ReminderRecurrence.Monthly, "2027-01-15", "2027-05-15", true, "the 15th recurs on the 15th" },
        // Documented AddMonths semantics, not a bug: a 31st anchor simply has no occurrence in a 30-day
        // month. It is NOT clamped to the 30th. Flipping this is a product decision, not a fix.
        { ReminderRecurrence.Monthly, "2027-01-31", "2027-04-30", false, "a 31st anchor has no occurrence in a 30-day month" },
        { ReminderRecurrence.Monthly, "2027-01-31", "2027-03-31", true, "…but it reappears in the next 31-day month" },
        { ReminderRecurrence.Quarterly, "2027-01-15", "2027-04-15", true, "15 Jan to 15 Apr is three months apart" },
        { ReminderRecurrence.Quarterly, "2027-01-15", "2027-03-15", false, "15 Mar is two months apart, not a quarter" },
        // Guards the (to.Year - from.Year) * 12 term: a MonthsApart that subtracted months alone would make
        // this 0 months apart, which is also 0 % 3, and the case would pass for the wrong reason — so the
        // 15 Mar row above is what proves the modulo is real, and this row proves the year term is.
        { ReminderRecurrence.Quarterly, "2027-01-15", "2028-01-15", true, "twelve months apart is still a whole number of quarters" },
        { ReminderRecurrence.Yearly, "2028-02-29", "2032-02-29", true, "a leap-day anchor recurs on the next leap day" },
        { ReminderRecurrence.Yearly, "2028-02-29", "2029-02-28", false, "29 February recurs on 29 February only — never on the 28th" }
    };

    [Theory(DisplayName = "OccursOn: which local day each cadence lands on, and at which instant")]
    [MemberData(nameof(Cadences))]
    public async Task Recurrence_LandsOnTheExpectedLocalDay(
        ReminderRecurrence recurrence, string anchorDate, string queryDate, bool expected, string because)
    {
        var anchor = Date(anchorDate);
        var query = Date(queryDate);

        await using (var seed = CreateDbContext())
            await ReminderSeedHelper.SeedReminderAsync(
                seed, UserId, Instant(anchor, AnchorTime), $"{recurrence} from {anchorDate}",
                recurrence: recurrence, ct: CancellationToken);

        var results = await DayViewAsync(query);

        if (!expected)
        {
            results.Should().BeEmpty(because);
            return;
        }

        var occurrence = results.Should().ContainSingle(because).Subject;
        AsUtc(occurrence.RemindAt).Should().Be(Instant(anchor, AnchorTime),
            "RemindAt is the stored anchor and must not be rewritten to this day");
        AsUtc(occurrence.OccursAt).Should().Be(Instant(query, AnchorTime),
            "OccursAt is the instant it fires on the requested day, at the anchor's wall-clock time in the user's zone");
        occurrence.Recurrence.Should().Be(recurrence);
    }

    [Fact(DisplayName = "A one-shot inside the local day is listed, with OccursAt equal to RemindAt")]
    public async Task OneShot_InsideTheLocalDay_IsListedAtItsOwnInstant()
    {
        var date = Date("2027-06-15");
        var remindAt = Instant(date, new TimeOnly(10, 0));

        await using (var seed = CreateDbContext())
            await ReminderSeedHelper.SeedReminderAsync(seed, UserId, remindAt, "Dentist", ct: CancellationToken);

        var occurrence = (await DayViewAsync(date)).Should().ContainSingle().Subject;

        AsUtc(occurrence.OccursAt).Should().Be(remindAt);
        AsUtc(occurrence.OccursAt).Should().Be(AsUtc(occurrence.RemindAt), "a one-shot occurs at the instant it stores");
        occurrence.Recurrence.Should().BeNull();
    }

    /// <summary>
    /// The case a UTC day boundary gets wrong. In a UTC+n zone the local day starts <i>before</i> midnight
    /// UTC, so 00:30 local on the 15th is stored as 22:30 UTC on the <b>14th</b>. An endpoint that ranged over
    /// the UTC day would file this reminder under the wrong date in both directions at once.
    /// </summary>
    [Fact(DisplayName = "A reminder whose stored instant falls on the previous UTC day is still on this local day")]
    public async Task OneShot_JustAfterLocalMidnight_BelongsToTheLocalDayNotTheUtcOne()
    {
        var date = Date("2027-06-15");
        var remindAt = Instant(date, new TimeOnly(0, 30));

        DateOnly.FromDateTime(remindAt).Should().Be(date.AddDays(-1),
            "the scenario only means anything if the stored instant really does land on the previous UTC day");

        await using (var seed = CreateDbContext())
            await ReminderSeedHelper.SeedReminderAsync(seed, UserId, remindAt, "Night shift", ct: CancellationToken);

        var occurrence = (await DayViewAsync(date)).Should().ContainSingle(
            "00:30 local on the 15th is the 15th, whatever UTC calls it").Subject;
        AsUtc(occurrence.OccursAt).Should().Be(remindAt);

        (await DayViewAsync(date.AddDays(-1))).Should().BeEmpty(
            "and it must not also show up on the 14th, which is where a UTC-day range would put it");
    }

    /// <summary>
    /// The birthday <c>ReminderRegistrationTests.PastAnchoredRecurringReminder_IsAccepted</c> lets in. Creating
    /// it is only half the feature — if the day view dropped anchors older than the requested day, the user
    /// would never see it again after the year they entered it.
    /// </summary>
    [Fact(DisplayName = "A yearly reminder anchored thirty years ago still appears on this year's day")]
    public async Task RecurringAnchoredInThePast_StillAppears()
    {
        var anchor = Date("1997-08-20");
        var query = Date("2027-08-20");

        await using (var seed = CreateDbContext())
            await ReminderSeedHelper.SeedReminderAsync(
                seed, UserId, Instant(anchor, AnchorTime), "Birthday",
                recurrence: ReminderRecurrence.Yearly, ct: CancellationToken);

        var occurrence = (await DayViewAsync(query)).Should().ContainSingle().Subject;

        AsUtc(occurrence.RemindAt).Should().Be(Instant(anchor, AnchorTime), "the anchor is 1997 and stays 1997");
        AsUtc(occurrence.OccursAt).Should().Be(Instant(query, AnchorTime), "but it fires in 2027");
    }

    /// <summary>
    /// Ordering is by <c>OccursAt</c> across <b>both</b> queries, which is the only thing making the two lists
    /// one day view — a sort applied per list, or on RemindAt, would file every recurring reminder by its
    /// anchor and scatter them through the day.
    /// </summary>
    [Fact(DisplayName = "One-shot and recurring reminders are interleaved in one list ordered by OccursAt")]
    public async Task DayView_OrdersBothKindsTogetherByOccursAt()
    {
        var date = Date("2027-06-15");

        await using (var seed = CreateDbContext())
        {
            // Deliberately seeded latest-first, so an unsorted response would come back in this order.
            await ReminderSeedHelper.SeedReminderAsync(seed, UserId, Instant(date, new TimeOnly(12, 0)), "Lunch", ct: CancellationToken);
            await ReminderSeedHelper.SeedReminderAsync(seed, UserId, Instant(date, new TimeOnly(10, 0)), "Dentist", ct: CancellationToken);
            await ReminderSeedHelper.SeedReminderAsync(
                seed, UserId, Instant(Date("2027-01-05"), new TimeOnly(8, 0)), "Pills",
                recurrence: ReminderRecurrence.Daily, ct: CancellationToken);

            // The recurring half is expanded in memory, after the SQL filter — a different code path from the
            // one-shot half, and the one where a stray IgnoreQueryFilters would leak. ByDate_ExcludesOtherUsersReminders
            // pins the one-shot half; this pins the expansion.
            await ReminderSeedHelper.EnsureOtherUserAsync(seed, CancellationToken);
            await ReminderSeedHelper.SeedReminderAsync(
                seed, ReminderSeedHelper.OtherUserId, Instant(Date("2027-01-05"), new TimeOnly(9, 0)), "Their pills",
                recurrence: ReminderRecurrence.Daily, ct: CancellationToken);
        }

        var results = await DayViewAsync(date);

        results.Select(r => r.Title).Should().Equal("Pills", "Dentist", "Lunch");
        results.Select(r => AsUtc(r.OccursAt)).Should().BeInAscendingOrder();
        results.Should().NotContain(r => r.Title == "Their pills",
            "another user's recurring reminder must not survive the in-memory expansion either");
    }
}

// =====================================================================================================
// Part 2 — the two places a wall-clock reading is turned into an instant across a DST boundary.
//
// ReminderRegistrationService.ComposeTaskInstant (the write path: a planner task's day + start time) and
// GetByDateReminderEndpoint.ToUtc (the read path: the requested day + a recurring anchor's time). They
// handle the spring-forward gap DIFFERENTLY — see DayView_OnTheSpringForwardDate_… below — which is worth
// reconciling, and is why both are pinned here rather than one standing in for the other.
// =====================================================================================================

[Collection("Postgres")]
public class ReminderDstCompositionTests(AppDbContextFixture fixture) : ReminderDayViewTestBase(fixture)
{
    /// <summary>Last Sunday of March 2027: 02:00 → 03:00, so 02:30 does not exist.</summary>
    private static readonly DateOnly SpringForward = new(2027, 3, 28);

    /// <summary>Last Sunday of October 2026: 03:00 → 02:00, so 02:30 happens twice.</summary>
    private static readonly DateOnly FallBack = new(2026, 10, 25);

    private static readonly TimeOnly HalfPastTwo = new(2, 30);

    // ---- A: the write path, spring forward -------------------------------------------------------

    [Fact(DisplayName = "A task starting in the spring-forward gap still saves, and its reminder lands at the first instant that exists")]
    public async Task ComposeTaskInstant_SpringForwardGap_ShiftsPastTheMissingHour()
    {
        var local = DateTime.SpecifyKind(SpringForward.ToDateTime(HalfPastTwo), DateTimeKind.Unspecified);
        Bratislava.IsInvalidTime(local).Should().BeTrue(
            "the whole case rests on 02:30 not existing that day; if the tz database ever moves the transition this test is testing nothing");

        await using var factory = CreateFactory(TestRoles.AdminAndUser, configOverrides: EmailOff);

        long taskId;
        await using (var seed = CreateDbContext())
            taskId = (await ReminderSeedHelper.SeedPlannerTaskAsync(seed, UserId, SpringForward, HalfPastTwo, ct: CancellationToken)).Id;

        var created = await factory.CreateClient().PostAsJsonAsync("api/reminder", new
        {
            Title = "Catch the night bus",
            RemindAt = DateTime.UtcNow.AddDays(30),
            LeadOffsetsMinutes = new[] { 0 },
            PlannerTaskId = taskId
        }, JsonOpts, CancellationToken);

        created.StatusCode.Should().Be(HttpStatusCode.Created,
            "the comment on ComposeTaskInstant is explicit that a DST gap must not make a task unsaveable");
        var reminderId = await created.Content.ReadFromJsonAsync<long>(CancellationToken);

        await using var db = CreateDbContext();
        var reminder = await db.Set<Reminder>().SingleAsync(r => r.Id == reminderId, CancellationToken);

        // 02:30 + the adjustment rule's DaylightDelta (one hour in this zone) = 03:30 local = 01:30Z.
        AsUtc(reminder.RemindAt).Should().Be(Instant(SpringForward, new TimeOnly(3, 30)),
            "the nudge moves to the first wall-clock time that exists, not to an arbitrary UTC instant");
    }

    // ---- B: the write path, fall back ------------------------------------------------------------

    /// <summary>
    /// <c>ConvertTimeToUtc</c> resolves an ambiguous local time as <b>standard</b> time, which in a European
    /// fall-back is the <i>second</i> pass of 02:30 (CET, UTC+1 → 01:30Z), not the first (CEST, UTC+2 →
    /// 00:30Z). <c>ComposeTaskInstant</c>'s own comment calls that "the earlier occurrence"; the label is
    /// wrong, the behaviour asserted here is what the code actually does. Pinned so a future
    /// <c>TimeZoneInfoOptions</c> change — or a switch to the genuinely earlier reading — is visible rather
    /// than silently moving every autumn reminder an hour.
    /// </summary>
    [Fact(DisplayName = "A task starting in the fall-back hour resolves to the standard-time pass of 02:30")]
    public async Task ComposeTaskInstant_FallBackAmbiguity_ResolvesToStandardTime()
    {
        var local = DateTime.SpecifyKind(FallBack.ToDateTime(HalfPastTwo), DateTimeKind.Unspecified);
        Bratislava.IsAmbiguousTime(local).Should().BeTrue("the case rests on 02:30 happening twice that day");

        await using var factory = CreateFactory(TestRoles.AdminAndUser, configOverrides: EmailOff);

        long taskId;
        await using (var seed = CreateDbContext())
            taskId = (await ReminderSeedHelper.SeedPlannerTaskAsync(seed, UserId, FallBack, HalfPastTwo, ct: CancellationToken)).Id;

        var created = await factory.CreateClient().PostAsJsonAsync("api/reminder", new
        {
            Title = "Long night",
            RemindAt = DateTime.UtcNow.AddDays(30),
            LeadOffsetsMinutes = new[] { 0 },
            PlannerTaskId = taskId
        }, JsonOpts, CancellationToken);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var reminderId = await created.Content.ReadFromJsonAsync<long>(CancellationToken);

        await using var db = CreateDbContext();
        var reminder = await db.Set<Reminder>().SingleAsync(r => r.Id == reminderId, CancellationToken);

        AsUtc(reminder.RemindAt).Should().Be(new DateTime(2026, 10, 25, 1, 30, 0, DateTimeKind.Utc),
            "02:30 CET — the standard-time reading ConvertTimeToUtc assumes");
        AsUtc(reminder.RemindAt).Should().NotBe(new DateTime(2026, 10, 25, 0, 30, 0, DateTimeKind.Utc),
            "02:30 CEST, the first of the two passes, is the reading the code is NOT taking");
    }

    // ---- C: the read path, spring forward --------------------------------------------------------

    /// <summary>
    /// <c>GetByDateReminderEndpoint.ToUtc</c> nudges an invalid local time forward by a <b>hard-coded hour</b>,
    /// where <c>ComposeTaskInstant</c> reads the adjustment rule's <c>DaylightDelta</c>. In Europe/Bratislava
    /// the two coincide, so this pins the read path's answer without being able to distinguish the two
    /// mechanisms — the divergence is only observable in a zone whose delta is not an hour (Lord Howe's is
    /// 30 minutes). Worth reconciling onto one implementation; until then, this is the behaviour.
    /// </summary>
    [Fact(DisplayName = "The day view on the spring-forward date answers 200 and nudges a 02:30 anchor past the missing hour")]
    public async Task DayView_OnTheSpringForwardDate_NudgesTheInvalidLocalTimeForward()
    {
        // A week before the transition, where 02:30 is an ordinary time — so the anchor itself is unremarkable
        // and only the requested day makes ToUtc's invalid-time branch run.
        var anchor = SpringForward.AddDays(-7);

        await using (var seed = CreateDbContext())
            await ReminderSeedHelper.SeedReminderAsync(
                seed, UserId, Instant(anchor, HalfPastTwo), "Night meds",
                recurrence: ReminderRecurrence.Daily, ct: CancellationToken);

        var occurrence = (await DayViewAsync(SpringForward)).Should().ContainSingle(
            "a missing hour must not 500 the day view, nor drop the day's reminders").Subject;

        AsUtc(occurrence.OccursAt).Should().Be(Instant(SpringForward, new TimeOnly(3, 30)),
            "02:30 does not exist that morning, so the occurrence is reported at 03:30 local");
    }
}

// =====================================================================================================
// Part 3 — SyncForPlannerTasksAsync fans out to every reminder on the task.
//
// PERF-3 was about SyncCoreAsync saving inside the per-reminder loop after the method had deliberately
// batch-loaded tasks and timezones. The loop now mutates the batch and saves once. This test is about the
// correctness that refactor has to preserve either way: all of the task's reminders move, not just the
// first, and the module-side definitions move with them.
// =====================================================================================================

[Collection("Postgres")]
public class ReminderTaskSyncFanOutTests(AppDbContextFixture fixture) : ReminderDayViewTestBase(fixture)
{
    [Fact(DisplayName = "Moving a task's start time moves every reminder attached to it, portal row and module definition alike")]
    public async Task MovingATask_MovesAllOfItsReminders()
    {
        var date = new DateOnly(2027, 5, 10);
        var originalStart = new TimeOnly(9, 0);
        var movedStart = new TimeOnly(14, 30);

        await using var factory = CreateFactory(TestRoles.AdminAndUser, configOverrides: EmailOff);
        var client = factory.CreateClient();

        long taskId;
        await using (var seed = CreateDbContext())
            taskId = (await ReminderSeedHelper.SeedPlannerTaskAsync(seed, UserId, date, originalStart, ct: CancellationToken)).Id;

        var reminderIds = new List<long>();
        foreach (var title in new[] { "Pack the bag", "Leave the house", "Badge in" })
        {
            var created = await client.PostAsJsonAsync("api/reminder", new
            {
                Title = title,
                RemindAt = DateTime.UtcNow.AddDays(30),
                LeadOffsetsMinutes = new[] { 0 },
                PlannerTaskId = taskId
            }, JsonOpts, CancellationToken);

            created.StatusCode.Should().Be(HttpStatusCode.Created);
            reminderIds.Add(await created.Content.ReadFromJsonAsync<long>(CancellationToken));
        }

        await using (var before = CreateDbContext())
        {
            var seeded = await before.Set<Reminder>().Where(r => reminderIds.Contains(r.Id)).ToListAsync(CancellationToken);
            seeded.Should().HaveCount(3);
            seeded.Select(r => AsUtc(r.RemindAt)).Should().AllBeEquivalentTo(Instant(date, originalStart),
                "all three are derived from the same task, so they start out on the same instant");
        }

        var moved = await client.PatchAsJsonAsync($"api/planner-task/{taskId}", new
        {
            StartTime = new { Hours = movedStart.Hour, Minutes = movedStart.Minute },
            EndTime = new { Hours = 15, Minutes = 30 }
        }, JsonOpts, CancellationToken);
        moved.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var expected = Instant(date, movedStart);

        await using var db = CreateDbContext();

        var reminders = await db.Set<Reminder>().Where(r => reminderIds.Contains(r.Id)).ToListAsync(CancellationToken);
        reminders.Select(r => AsUtc(r.RemindAt)).Should().AllBeEquivalentTo(expected,
            "the batch mutates every reminder on the task and then saves — a reminder left behind is one the loop stopped short of");

        // The registry half of the seam: a portal row moved without its definition re-registered would fire
        // the user's nudge at the old time and look perfectly correct in the database.
        var subjectIds = reminderIds.Select(id => id.ToString()).ToList();
        var definitions = await db.Set<ReminderDefinition>()
            .Where(d => d.OwnerModule == ReminderRegistrationService.OwnerModule
                        && d.SubjectType == ReminderRegistrationService.SubjectType
                        && d.Kind == ReminderRegistrationService.Kind
                        && subjectIds.Contains(d.SubjectId))
            .ToListAsync(CancellationToken);

        definitions.Should().HaveCount(3, "re-registration is an upsert on the same key, so there is still one definition each");
        definitions.Select(d => d.DueAt).Should().AllBeEquivalentTo(new DateTimeOffset(expected, TimeSpan.Zero));
    }
}
