using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.Core.domain.model.@enum;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.IntegrationTests.Reminders;
using AdhdTimeOrganizer.Planning.application.dto.response.suggestion;
using AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// F4-02 — a named stretch of time away (<c>/time-away</c>) and the stamp it puts on its days.
/// <para>
/// Asserted on <c>calendar</c> and <c>time_away</c> rows, never on a status alone: every way the stamp goes wrong —
/// a day left unlinked, an override overwritten, a released day still marked away — answers a perfectly good 2xx,
/// and the only consumers that would notice (Routines' excusal, FO6's withdrawal) log and swallow.
/// </para>
/// <para>The fixture user is on UTC. Dates are fixed far in the future unless "today" is the point.</para>
/// </summary>
[Collection("Postgres")]
public class TimeAwayTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    // Monday 29 July 2030 to Sunday 4 August 2030: a week across a month boundary, with a weekend in it.
    private static readonly DateOnly TripStart = new(2030, 7, 29);
    private static readonly DateOnly TripEnd = new(2030, 8, 4);

    // ─── The stamp ───────────────────────────────────────────────────────────

    [Fact(DisplayName = "F4-02: creating a stretch stamps every day of its range and creates the missing ones")]
    public async Task Create_StampsEveryDay_AndProvisionsMissingOnes()
    {
        // Two of the seven days exist already; the other five do not.
        await using (var db = CreateDbContext())
        {
            await PlanningTestSeedHelper.SeedCalendarAsync(db, TripStart, UserId, CancellationToken);
            await PlanningTestSeedHelper.SeedCalendarAsync(db, TripEnd, UserId, CancellationToken, DayType.Weekend);
        }

        var stamp = await CreateAsync(Body("Barcelona", "Vacation", TripStart, TripEnd, location: "Travel"));

        stamp.Days.Select(d => d.Date).Should().Equal(Dates(TripStart, TripEnd), "one entry per day, in order");
        stamp.Days.Should().OnlyContain(d => d.CalendarId > 0 && d.DayType == DayType.Vacation && !d.KeptOwnDayType);
        stamp.ReleasedDates.Should().BeEmpty();

        var days = await DaysAsync(TripStart, TripEnd);
        days.Should().HaveCount(7, "the five missing days were created");
        days.Should().OnlyContain(d => d.TimeAwayId == stamp.Id && d.DayType == DayType.Vacation && d.Location == Location.Travel,
            "weekends included: a Saturday in Barcelona is away");

        var stretch = await StretchAsync(stamp.Id);
        (stretch.Name, stretch.Kind, stretch.StartDate, stretch.EndDate).Should().Be(("Barcelona", DayType.Vacation, TripStart, TripEnd));
    }

    [Fact(DisplayName = "F4-02: a day's own override of another kind, and a location it already had, survive the stamp")]
    public async Task Stamp_KeepsAnOverrideOfAnotherKind_AndALocationAlreadySet()
    {
        var special = TripStart.AddDays(1);
        var sick = TripStart.AddDays(2);
        var atHome = TripStart.AddDays(3);
        await using (var db = CreateDbContext())
        {
            await PlanningTestSeedHelper.SeedCalendarAsync(db, special, UserId, CancellationToken, DayType.Special);
            await PlanningTestSeedHelper.SeedCalendarAsync(db, sick, UserId, CancellationToken, DayType.SickDay);
            await PlanningTestSeedHelper.SeedCalendarAsync(db, atHome, UserId, CancellationToken);
            (await db.Set<Calendar>().IgnoreQueryFilters().SingleAsync(c => c.UserId == UserId && c.Date == atHome, CancellationToken))
                .Location = Location.Home;
            await db.SaveChangesAsync(CancellationToken);
        }

        var stamp = await CreateAsync(Body("Barcelona", "Vacation", TripStart, TripEnd, location: "Travel"));

        stamp.Days.Where(d => d.KeptOwnDayType).Select(d => (d.Date, d.DayType))
            .Should().BeEquivalentTo([(special, DayType.Special), (sick, DayType.SickDay)]);

        var days = (await DaysAsync(TripStart, TripEnd)).ToDictionary(d => d.Date);
        days.Values.Should().OnlyContain(d => d.TimeAwayId == stamp.Id, "an overridden day still belongs to the trip");
        days[special].DayType.Should().Be(DayType.Special);
        days[sick].DayType.Should().Be(DayType.SickDay, "a sick day inside a vacation is that day's own override");
        days[atHome].DayType.Should().Be(DayType.Vacation);
        days[atHome].Location.Should().Be(Location.Home, "the stamp fills a location only where there is none");
        days[TripStart].Location.Should().Be(Location.Travel);
    }

    [Fact(DisplayName = "F4-02: an overlapping stretch is a 400 naming the other one, and writes nothing; an adjacent one is fine")]
    public async Task Overlap_Is400_AndWritesNothing()
    {
        var first = await CreateAsync(Body("Barcelona", "Vacation", TripStart, TripEnd));

        var later = TripEnd.AddDays(3);
        var response = await CreateClient().PostAsJsonAsync("api/time-away",
            Body("Flu", "SickDay", TripEnd, later), JsonOpts, CancellationToken);
        var text = await response.Content.ReadAsStringAsync(CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, text);
        var errors = JsonDocument.Parse(text).RootElement.GetProperty("errors").EnumerateObject().ToList();
        errors.Should().ContainSingle(e => e.Name.Equals("startDate", StringComparison.OrdinalIgnoreCase),
            "the client shows the overlap on the range field");
        text.Should().Contain("Barcelona", "the message names the stretch it runs into");

        await using (var db = CreateDbContext())
        {
            (await db.Set<TimeAway>().IgnoreQueryFilters().CountAsync(t => t.UserId == UserId, CancellationToken)).Should().Be(1);
            (await db.Set<Calendar>().IgnoreQueryFilters()
                    .AnyAsync(c => c.UserId == UserId && c.Date > TripEnd && c.Date <= later, CancellationToken))
                .Should().BeFalse("a refused stretch provisions no day");
        }
        (await DaysAsync(TripEnd, TripEnd)).Single().TimeAwayId.Should().Be(first.Id, "the first stretch's day is untouched");

        // The day after the trip ends is free: inclusive ranges that do not share a day do not overlap.
        var adjacent = await CreateAsync(Body("Flu", "SickDay", TripEnd.AddDays(1), later));
        (await DaysAsync(TripEnd.AddDays(1), later)).Should().OnlyContain(d => d.TimeAwayId == adjacent.Id && d.DayType == DayType.SickDay);
    }

    [Fact(DisplayName = "F4-02: a resize releases the days that leave and stamps the days that join")]
    public async Task Resize_ReleasesTheDaysThatLeave_AndStampsTheDaysThatJoin()
    {
        var created = await CreateAsync(Body("Barcelona", "Vacation", TripStart, TripStart.AddDays(4), location: "Travel"));

        // The user marks one of the days that is about to leave as Special by hand: it must stay Special.
        var specialDay = TripStart.AddDays(1);
        await SetDayTypeAsync(specialDay, DayType.Special);

        var newStart = TripStart.AddDays(3);
        var newEnd = TripStart.AddDays(6);
        var response = await CreateClient().PutAsJsonAsync($"api/time-away/{created.Id}",
            Body("Barcelona", "Vacation", newStart, newEnd, location: "Travel"), JsonOpts, CancellationToken);
        var text = await response.Content.ReadAsStringAsync(CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, text);
        var stamp = JsonSerializer.Deserialize<TimeAwayStampResponse>(text, JsonOpts)!;

        stamp.Days.Select(d => d.Date).Should().Equal(Dates(newStart, newEnd));
        stamp.ReleasedDates.Should().Equal(Dates(TripStart, newStart.AddDays(-1)));

        var days = (await DaysAsync(TripStart, newEnd)).ToDictionary(d => d.Date);
        foreach (var released in Dates(TripStart, newStart.AddDays(-1)))
        {
            days[released].TimeAwayId.Should().BeNull($"{released} left the stretch");
            days[released].Location.Should().BeNull("the location the stamp wrote leaves with it");
        }
        days[TripStart].DayType.Should().Be(DayType.Workday, "back to its natural type");
        days[specialDay].DayType.Should().Be(DayType.Special, "the user's own override is not the stamp's to undo");

        foreach (var held in Dates(newStart, newEnd))
        {
            days[held].TimeAwayId.Should().Be(created.Id);
            days[held].DayType.Should().Be(DayType.Vacation, $"{held} is in the new range, weekend or not");
        }
    }

    [Fact(DisplayName = "F4-02: changing a stretch's kind re-stamps its days with the new kind")]
    public async Task ChangingKind_ReStampsTheDays()
    {
        var created = await CreateAsync(Body("Away", "Vacation", TripStart, TripStart.AddDays(2)));

        var response = await CreateClient().PutAsJsonAsync($"api/time-away/{created.Id}",
            Body("Flu after all", "SickDay", TripStart, TripStart.AddDays(2)), JsonOpts, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(CancellationToken));

        (await DaysAsync(TripStart, TripStart.AddDays(2)))
            .Should().OnlyContain(d => d.DayType == DayType.SickDay && d.TimeAwayId == created.Id,
                "un-stamped by the old kind and stamped by the new, not left on Vacation");
    }

    [Fact(DisplayName = "F4-02: deleting a stretch restores natural types and keeps a day the user overrode")]
    public async Task Delete_RestoresNaturalTypes_AndKeepsAUserOverride()
    {
        var created = await CreateAsync(Body("Barcelona", "Vacation", TripStart, TripEnd, location: "Travel"));

        // Ill on the Wednesday of the trip, marked afterwards.
        var sickDay = TripStart.AddDays(2);
        await SetDayTypeAsync(sickDay, DayType.SickDay);

        var response = await CreateClient().DeleteAsync($"api/time-away/{created.Id}", CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync(CancellationToken));

        await using (var db = CreateDbContext())
            (await db.Set<TimeAway>().IgnoreQueryFilters().AnyAsync(t => t.Id == created.Id, CancellationToken)).Should().BeFalse();

        var days = await DaysAsync(TripStart, TripEnd);
        days.Should().HaveCount(7, "the days themselves stay — deleting a label for a stretch never deletes days");
        days.Should().OnlyContain(d => d.TimeAwayId == null && d.Location == null);
        foreach (var day in days.Where(d => d.Date != sickDay))
            day.DayType.Should().Be(day.Date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? DayType.Weekend : DayType.Workday);
        days.Single(d => d.Date == sickDay).DayType.Should().Be(DayType.SickDay, "the user's own override survives the delete");
    }

    [Fact(DisplayName = "F4-02: deleting a stretch never deletes a task planned inside it")]
    public async Task Delete_LeavesTasksAlone()
    {
        var created = await CreateAsync(Body("Barcelona", "Vacation", TripStart, TripEnd));

        long taskId;
        await using (var db = CreateDbContext())
        {
            var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, $"beach-{Guid.NewGuid():N}", UserId, CancellationToken);
            var calendarId = (await DaysAsync(TripStart, TripStart)).Single().Id;
            taskId = await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, calendarId, new TimeOnly(10, 0), new TimeOnly(12, 0), UserId, ct: CancellationToken);
        }

        (await CreateClient().DeleteAsync($"api/time-away/{created.Id}", CancellationToken)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var verify = CreateDbContext();
        (await verify.Set<PlannerTask>().IgnoreQueryFilters().AnyAsync(t => t.Id == taskId, CancellationToken)).Should().BeTrue();
    }

    // ─── What the stamp reaches through the event ────────────────────────────

    /// <summary>
    /// Owner decision 2026-10-12 (F4 Q7): deleting a stretch keeps the routine cycles it excused excused. The excusal
    /// itself is reached only through <c>CalendarDayTypesChangedEvent</c> — which the stamper publishes and Routines'
    /// handler logs-and-swallows — so the completion row is the only evidence either half ran.
    /// </summary>
    [Fact(DisplayName = "F4-02: a stretch over past days excuses their routine cycles, and deleting it leaves them excused")]
    public async Task Stretch_ExcusesPastCycles_AndDeleteLeavesThemExcused()
    {
        var sickDay = new DateOnly(2026, 3, 4);
        long dailyId;
        await using (var db = CreateDbContext())
        {
            var daily = new RoutineTimePeriod
            {
                UserId = UserId,
                Text = "Time-away daily",
                Color = "#123456",
                LengthInDays = 1,
                ResetAnchorDay = 0,
                StreakThreshold = 100,
                StreakGraceDays = 0,
                // Reset an hour ago, so no reset is due and only the retroactive path can touch the period.
                LastResetAt = DateTime.UtcNow.AddHours(-1),
                Streak = 0,
                BestStreak = 2
            };
            db.Set<RoutineTimePeriod>().Add(daily);
            await db.SaveChangesAsync(CancellationToken);
            dailyId = daily.Id;

            db.Set<RoutinePeriodCompletion>().AddRange(
                Completion(dailyId, sickDay.AddDays(-2), completed: 1),
                Completion(dailyId, sickDay.AddDays(-1), completed: 1),
                Completion(dailyId, sickDay, completed: 0));
            await db.SaveChangesAsync(CancellationToken);
        }

        // No calendar row exists for the day: the stamp creates it, and a created day counts as a moved one.
        var created = await CreateAsync(Body("Flu", "SickDay", sickDay, sickDay));

        await AssertExcusedAsync();

        (await CreateClient().DeleteAsync($"api/time-away/{created.Id}", CancellationToken)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        await AssertExcusedAsync("deleting the stretch must not re-score history (F4 Q7)");
        (await DaysAsync(sickDay, sickDay)).Single().DayType.Should().Be(DayType.Workday, "the day itself is un-stamped");

        async Task AssertExcusedAsync(string because = "")
        {
            await using var verify = CreateDbContext();
            (await verify.Set<RoutinePeriodCompletion>().SingleAsync(c => c.TimePeriodId == dailyId && c.PeriodStart == sickDay, CancellationToken))
                .IsExcused.Should().BeTrue(because);
            (await verify.Set<RoutineTimePeriod>().SingleAsync(p => p.Id == dailyId, CancellationToken))
                .Streak.Should().Be(2, because);
        }
    }

    /// <summary>
    /// FO6 places today's and tomorrow's occurrences the moment a day-plan is read, so a flu marked tonight finds them
    /// already there. A stretch reaches <c>AwayDayOccurrenceWithdrawalHandler</c> through the same event as the bulk
    /// bar, which is the whole of what lets the two features meet.
    /// </summary>
    [Fact(DisplayName = "F4-02: a stretch over today and tomorrow withdraws the untouched auto-placed occurrences")]
    public async Task StretchOverTodayAndTomorrow_WithdrawsUntouchedOccurrences()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        long ruleId;
        await using (var db = CreateDbContext())
        {
            var rule = new RepeatingPlannerTask
            {
                UserId = UserId,
                ActivityId = await PlanningTestSeedHelper.SeedActivityAsync(db, $"gym-{Guid.NewGuid():N}", UserId, CancellationToken),
                StartTime = new TimeOnly(18, 0),
                EndTime = new TimeOnly(19, 0),
                IsBackground = false,
                IsActive = true,
                AutoPlace = true,
                RecurrenceType = RecurrenceType.DayOfWeek,
                ScheduledDays = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"]
            };
            db.Set<RepeatingPlannerTask>().Add(rule);
            await db.SaveChangesAsync(CancellationToken);
            ruleId = rule.Id;
        }

        (await CreateClient().GetAsync($"api/calendar/day-plan/{today:yyyy-MM-dd}", CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await OccurrenceDatesAsync(ruleId)).Should().BeEquivalentTo([today, today.AddDays(1)], "the read placed both days");

        await CreateAsync(Body("Flu", "SickDay", today, today.AddDays(1)));

        (await OccurrenceDatesAsync(ruleId)).Should().BeEmpty("both days are away now, and neither block was touched");
    }

    // ─── Suggestions ─────────────────────────────────────────────────────────

    [Fact(DisplayName = "F4-02: inside a stretch with a template, that template is suggested first, as pattern type 4")]
    public async Task Suggestions_TheStretchTemplateRanksFirst_AsType4()
    {
        long beachId, officeId, tripPlanId;
        await using (var db = CreateDbContext())
        {
            // On a Monday marked Vacation these two would otherwise lead: a day-type match, then a weekday match.
            beachId = await PlanningTestSeedHelper.SeedTaskPlannerDayTemplateAsync(db, UserId, "Beach", CancellationToken, DayType.Vacation);
            officeId = await PlanningTestSeedHelper.SeedTaskPlannerDayTemplateAsync(db, UserId, "Office", CancellationToken,
                scheduledDays: [TripStart.DayOfWeek]);
            tripPlanId = await PlanningTestSeedHelper.SeedTaskPlannerDayTemplateAsync(db, UserId, "City walks", CancellationToken,
                DayType.Special);
        }

        var stamp = await CreateAsync(Body("Barcelona", "Vacation", TripStart, TripEnd, templateId: tripPlanId));

        var inside = await SuggestionsAsync(stamp.Days.First().CalendarId);
        inside.First().Should().BeEquivalentTo(new { PatternType = 4, PatternLabel = "Barcelona", OccurrenceCount = 0 });
        inside.First().Template.Id.Should().Be(tripPlanId);
        inside.Select(s => s.Template.Id).Should().OnlyHaveUniqueItems().And.Contain([beachId, officeId],
            "the stretch's template goes first; the others are still offered after it");

        // The day after the trip, which is the same weekday's neighbour and no part of the stretch: no type 4.
        long outsideId;
        await using (var db = CreateDbContext())
            outsideId = await PlanningTestSeedHelper.SeedCalendarAsync(db, TripEnd.AddDays(1), UserId, CancellationToken);
        (await SuggestionsAsync(outsideId)).Should().NotContain(s => s.PatternType == 4);
    }

    // ─── Ownership and validation ────────────────────────────────────────────

    [Fact(DisplayName = "F4-02: another user's TemplateId is a 404, and writes nothing")]
    public async Task ForeignTemplate_Is404_AndWritesNothing()
    {
        long theirTemplateId;
        await using (var db = CreateDbContext())
        {
            await ReminderSeedHelper.EnsureOtherUserAsync(db, CancellationToken);
            theirTemplateId = await PlanningTestSeedHelper.SeedTaskPlannerDayTemplateAsync(db, ReminderSeedHelper.OtherUserId, ct: CancellationToken);
        }

        var response = await CreateClient().PostAsJsonAsync("api/time-away",
            Body("Barcelona", "Vacation", TripStart, TripEnd, templateId: theirTemplateId), JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, await response.Content.ReadAsStringAsync(CancellationToken));
        await using var verify = CreateDbContext();
        (await verify.Set<TimeAway>().IgnoreQueryFilters().AnyAsync(t => t.UserId == UserId, CancellationToken)).Should().BeFalse();
        (await verify.Set<Calendar>().IgnoreQueryFilters().AnyAsync(c => c.UserId == UserId && c.Date >= TripStart, CancellationToken))
            .Should().BeFalse("a refused stretch provisions no day");
    }

    [Fact(DisplayName = "F4-02: another user's stretch cannot be read, updated or deleted")]
    public async Task AnotherUsersStretch_IsA404Everywhere()
    {
        long theirsId;
        await using (var db = CreateDbContext())
        {
            await ReminderSeedHelper.EnsureOtherUserAsync(db, CancellationToken);
            var theirs = new TimeAway
            {
                UserId = ReminderSeedHelper.OtherUserId, Name = "Theirs", Kind = DayType.Vacation, StartDate = TripStart, EndDate = TripEnd
            };
            db.Set<TimeAway>().Add(theirs);
            await db.SaveChangesAsync(CancellationToken);
            theirsId = theirs.Id;
        }

        var client = CreateClient();
        (await client.GetAsync($"api/time-away/{theirsId}", CancellationToken)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.PutAsJsonAsync($"api/time-away/{theirsId}", Body("Mine now", "SickDay", TripStart, TripEnd), JsonOpts, CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.DeleteAsync($"api/time-away/{theirsId}", CancellationToken)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var listed = await ListAsync(new { });
        listed.Should().NotContain(t => t.Id == theirsId);

        // And their stretch does not stop me marking the same dates: overlap is per user.
        await CreateAsync(Body("Mine", "Vacation", TripStart, TripEnd));

        await using var verify = CreateDbContext();
        (await verify.Set<TimeAway>().IgnoreQueryFilters().SingleAsync(t => t.Id == theirsId, CancellationToken))
            .Name.Should().Be("Theirs");
    }

    [Theory(DisplayName = "F4-02: the validator refuses what a stretch cannot be")]
    [InlineData("Special", "2030-07-29", "2030-08-04")]  // not an away kind
    [InlineData("Workday", "2030-07-29", "2030-08-04")]
    [InlineData("Vacation", "2030-08-04", "2030-07-29")] // ends before it starts
    [InlineData("Vacation", "2030-01-01", "2031-01-02")] // 367 days
    public async Task Validator_Refuses(string kind, string start, string end)
    {
        var response = await CreateClient().PostAsJsonAsync("api/time-away",
            Body("Bad", kind, DateOnly.Parse(start), DateOnly.Parse(end)), JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync(CancellationToken));
        await using var verify = CreateDbContext();
        (await verify.Set<TimeAway>().IgnoreQueryFilters().AnyAsync(t => t.UserId == UserId, CancellationToken)).Should().BeFalse();
    }

    [Fact(DisplayName = "F4-02: a year-long stretch (366 days, the ceiling) is accepted")]
    public async Task Validator_AcceptsTheCeiling()
    {
        var stamp = await CreateAsync(Body("Sabbatical", "Vacation", new DateOnly(2032, 1, 1), new DateOnly(2032, 12, 31)));
        stamp.Days.Should().HaveCount(366, "2032 is a leap year, and the cap counts inclusively");
    }

    // ─── Reads ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "F4-02: the day reads carry the stretch's id and name; the list filters by range")]
    public async Task Reads_CarryTheStretch_AndTheListFiltersByRange()
    {
        long templateId;
        await using (var db = CreateDbContext())
            templateId = await PlanningTestSeedHelper.SeedTaskPlannerDayTemplateAsync(db, UserId, "City walks", CancellationToken);

        var trip = await CreateAsync(Body("Barcelona", "Vacation", TripStart, TripEnd, templateId: templateId, notes: "Flight 07:40"));
        var flu = await CreateAsync(Body("Flu", "SickDay", new DateOnly(2030, 9, 10), new DateOnly(2030, 9, 12)));

        // calendar/filter is what the month grid reads.
        var month = await CreateClient().PostAsJsonAsync("api/calendar/filter",
            new { From = TripStart, Until = TripEnd.AddDays(1) }, JsonOpts, CancellationToken);
        month.StatusCode.Should().Be(HttpStatusCode.OK, await month.Content.ReadAsStringAsync(CancellationToken));
        var days = (await month.Content.ReadFromJsonAsync<List<CalendarResponse>>(JsonOpts, CancellationToken))!;
        days.Where(d => d.Date <= TripEnd).Should().OnlyContain(d => d.TimeAwayId == trip.Id && d.TimeAwayName == "Barcelona");
        days.Where(d => d.Date > TripEnd).Should().OnlyContain(d => d.TimeAwayId == null && d.TimeAwayName == null);

        // The day-plan read carries the same pair.
        var plan = await CreateClient().GetAsync($"api/calendar/day-plan/{TripStart:yyyy-MM-dd}", CancellationToken);
        var planBody = JsonDocument.Parse(await plan.Content.ReadAsStringAsync(CancellationToken)).RootElement;
        planBody.GetProperty("calendar").GetProperty("timeAwayName").GetString().Should().Be("Barcelona");

        var all = await ListAsync(new { });
        all.Select(t => t.Id).Should().Equal([trip.Id, flu.Id], "oldest first");
        all[0].Should().BeEquivalentTo(new
        {
            Name = "Barcelona", Kind = DayType.Vacation, StartDate = TripStart, EndDate = TripEnd,
            TemplateId = (long?)templateId, TemplateName = "City walks", Notes = "Flight 07:40"
        });

        (await ListAsync(new { From = TripEnd, Until = TripEnd })).Select(t => t.Id).Should().Equal([trip.Id], "a range touching one day of it");
        (await ListAsync(new { From = new DateOnly(2030, 9, 1) })).Select(t => t.Id).Should().Equal([flu.Id]);
        (await ListAsync(new { Until = new DateOnly(2030, 8, 31) })).Select(t => t.Id).Should().Equal([trip.Id]);

        var byId = await CreateClient().GetFromJsonAsync<TimeAwayResponse>($"api/time-away/{flu.Id}", JsonOpts, CancellationToken);
        byId!.Name.Should().Be("Flu");
    }

    // ─── helpers ─────────────────────────────────────────────────────────────

    private static object Body(
        string name, string kind, DateOnly start, DateOnly end,
        long? templateId = null, string? location = null, string? notes = null) =>
        new { Name = name, Kind = kind, StartDate = start, EndDate = end, TemplateId = templateId, Location = location, Notes = notes };

    private static List<DateOnly> Dates(DateOnly from, DateOnly to)
    {
        var dates = new List<DateOnly>();
        for (var d = from; d <= to; d = d.AddDays(1))
            dates.Add(d);
        return dates;
    }

    private async Task<TimeAwayStampResponse> CreateAsync(object body)
    {
        var response = await CreateClient().PostAsJsonAsync("api/time-away", body, JsonOpts, CancellationToken);
        var text = await response.Content.ReadAsStringAsync(CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created, text);
        return JsonSerializer.Deserialize<TimeAwayStampResponse>(text, JsonOpts)!;
    }

    private async Task<List<TimeAwayResponse>> ListAsync(object filter)
    {
        var response = await CreateClient().PostAsJsonAsync("api/time-away/filter", filter, JsonOpts, CancellationToken);
        var text = await response.Content.ReadAsStringAsync(CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, text);
        return JsonSerializer.Deserialize<List<TimeAwayResponse>>(text, JsonOpts)!;
    }

    private async Task<List<TemplateSuggestionResponse>> SuggestionsAsync(long calendarId)
    {
        var response = await CreateClient().GetAsync($"api/task-planner-day-template/suggestions/{calendarId}", CancellationToken);
        var text = await response.Content.ReadAsStringAsync(CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, text);
        return JsonSerializer.Deserialize<List<TemplateSuggestionResponse>>(text, JsonOpts)!;
    }

    private async Task<List<Calendar>> DaysAsync(DateOnly from, DateOnly to)
    {
        await using var db = CreateDbContext();
        return await db.Set<Calendar>().IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.UserId == UserId && c.Date >= from && c.Date <= to)
            .OrderBy(c => c.Date)
            .ToListAsync(CancellationToken);
    }

    private async Task<TimeAway> StretchAsync(long id)
    {
        await using var db = CreateDbContext();
        return await db.Set<TimeAway>().IgnoreQueryFilters().AsNoTracking().SingleAsync(t => t.Id == id, CancellationToken);
    }

    /// <summary>A day type set by hand, straight on the row — the user's own override, not the stamp's.</summary>
    private async Task SetDayTypeAsync(DateOnly date, DayType dayType)
    {
        await using var db = CreateDbContext();
        var day = await db.Set<Calendar>().IgnoreQueryFilters().SingleAsync(c => c.UserId == UserId && c.Date == date, CancellationToken);
        day.DayType = dayType;
        await db.SaveChangesAsync(CancellationToken);
    }

    private async Task<List<DateOnly>> OccurrenceDatesAsync(long ruleId)
    {
        await using var db = CreateDbContext();
        return await db.Set<PlannerTask>().IgnoreQueryFilters().AsNoTracking()
            .Where(t => t.SourceRepeatingTaskId == ruleId)
            .Select(t => t.Calendar.Date)
            .ToListAsync(CancellationToken);
    }

    private static RoutinePeriodCompletion Completion(long periodId, DateOnly start, int completed) =>
        new()
        {
            TimePeriodId = periodId,
            PeriodStart = start,
            PeriodEnd = start.AddDays(1),
            CompletedCount = completed,
            TotalCount = 1
        };
}
