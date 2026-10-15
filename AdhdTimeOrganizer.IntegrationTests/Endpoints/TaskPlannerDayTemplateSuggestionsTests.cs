using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.Core.domain.model.@enum;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Planning.application.dto.response.suggestion;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// TEST-13 — <c>GetSuggestionsTaskPlannerDayTemplateEndpoint</c>, which had no test at all: a standing known
/// gap in <c>docs/testing.md</c>, and the reason a broken projection here was silent.
/// <para>
/// Its source is <c>mv_template_suggestion_pattern</c>, aggregated from <c>calendar.applied_template_id</c>
/// alone — two applications of one template on the same ISO weekday make a <c>PatternType 0</c> row, two on
/// the same <c>DayType</c> make a <c>PatternType 1</c> row. The fixtures below vary the day type across the
/// two source days (or the weekday) on purpose, so each test sees only the pattern kind it is about.
/// </para>
/// <para>
/// B27 added a second tier after the history one: an active template whose own <c>ScheduledDays</c> or
/// <c>SuggestedForDayType</c> matches the day is suggested with no history (<c>PatternType 2</c> / <c>3</c>).
/// The history tests therefore seed through <see cref="SeedHistoryOnlyTemplateAsync"/>, whose metadata
/// matches none of their days — otherwise the seed helper's default <c>Workday</c> tag would add a row.
/// </para>
/// <para>
/// <c>CQ-13</c> is the last test here: <c>applied_template_id</c> carries <b>no foreign key</b>, so deleting a
/// template leaves the stamp dangling and the view row outlives its principal until the host-side refresh job
/// runs again. Projecting through the null navigation was a 500 on a page load, caused by an action the user
/// had just taken themselves.
/// </para>
/// </summary>
[Collection("Postgres")]
public class TaskPlannerDayTemplateSuggestionsTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    [Fact(DisplayName = "A template applied on two Tuesdays suggests itself for a Tuesday and not for a Wednesday")]
    public async Task DayOfWeekPattern_SuggestsOnTheSameWeekdayOnly()
    {
        var target = new DateOnly(2028, 4, 4); // Tuesday
        long templateId, tuesdayCalendarId, wednesdayCalendarId;

        await using (var db = CreateDbContext())
        {
            templateId = await SeedHistoryOnlyTemplateAsync(db, "Tuesday routine");
            await StampAsync(db, templateId, target.AddDays(-7), DayType.Workday);
            await StampAsync(db, templateId, target.AddDays(-14), DayType.Vacation); // differing day types: no DayType pattern
            tuesdayCalendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, target, UserId, CancellationToken);
            wednesdayCalendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, target.AddDays(1), UserId, CancellationToken);
        }

        await RefreshTemplateSuggestionViewAsync();

        var onTuesday = await GetSuggestionsAsync(tuesdayCalendarId);
        var suggestion = onTuesday.Should().ContainSingle().Subject;
        suggestion.Template.Id.Should().Be(templateId);
        suggestion.PatternType.Should().Be(0);
        suggestion.OccurrenceCount.Should().Be(2);
        suggestion.PatternLabel.Should().Be(target.DayOfWeek.ToString());

        (await GetSuggestionsAsync(wednesdayCalendarId)).Should().BeEmpty("the pattern is for Tuesdays");
    }

    /// <summary>
    /// The off-by-one case. <c>DateOnly.DayOfWeek</c> is 0 for Sunday and the endpoint maps it to ISO 7, while
    /// <c>DayNames</c> is Monday-indexed and subtracts 1 — so Sunday is where a wrong mapping becomes an
    /// <c>IndexOutOfRangeException</c> rather than a merely wrong label.
    /// </summary>
    [Fact(DisplayName = "A Sunday pattern is labelled Sunday, not off by one")]
    public async Task DayOfWeekPattern_HandlesSunday()
    {
        var target = new DateOnly(2028, 4, 2); // Sunday
        long templateId, calendarId;

        await using (var db = CreateDbContext())
        {
            templateId = await SeedHistoryOnlyTemplateAsync(db, "Sunday routine");
            await StampAsync(db, templateId, target.AddDays(-7), DayType.Workday);
            await StampAsync(db, templateId, target.AddDays(-14), DayType.Vacation);
            calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, target, UserId, CancellationToken);
        }

        await RefreshTemplateSuggestionViewAsync();

        var suggestion = (await GetSuggestionsAsync(calendarId)).Should().ContainSingle().Subject;
        suggestion.PatternType.Should().Be(0);
        suggestion.PatternLabel.Should().Be("Sunday");
    }

    /// <summary>
    /// The day-type tier. <c>DayType</c> has five members and no <c>Holiday</c> — a public holiday is a
    /// <c>Workday</c> row carrying a <c>HolidayName</c> — so the label is the enum name of one of the five.
    /// </summary>
    [Fact(DisplayName = "A template applied on two Vacation days suggests itself for a Vacation day, labelled with the day type")]
    public async Task DayTypePattern_SuggestsOnTheSameDayType()
    {
        var target = new DateOnly(2028, 4, 4);
        long templateId, vacationCalendarId, workdayCalendarId;

        await using (var db = CreateDbContext())
        {
            templateId = await SeedHistoryOnlyTemplateAsync(db, "Holiday routine");
            // Two different weekdays, so only the DayType grouping reaches the threshold.
            await StampAsync(db, templateId, target.AddDays(-3), DayType.Vacation);
            await StampAsync(db, templateId, target.AddDays(-9), DayType.Vacation);
            vacationCalendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, target, UserId, CancellationToken, DayType.Vacation);
            workdayCalendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, target.AddDays(1), UserId, CancellationToken);
        }

        await RefreshTemplateSuggestionViewAsync();

        var suggestion = (await GetSuggestionsAsync(vacationCalendarId)).Should().ContainSingle().Subject;
        suggestion.PatternType.Should().Be(1);
        suggestion.PatternLabel.Should().Be(nameof(DayType.Vacation));
        suggestion.Template.Id.Should().Be(templateId);

        (await GetSuggestionsAsync(workdayCalendarId)).Should().BeEmpty("a Workday is not a Vacation");
    }

    [Fact(DisplayName = "Suggestions come back with the most-applied template first")]
    public async Task Suggestions_AreOrderedByOccurrenceCountDescending()
    {
        var target = new DateOnly(2028, 4, 4);
        long usedTwice, usedThreeTimes, calendarId;

        await using (var db = CreateDbContext())
        {
            usedTwice = await SeedHistoryOnlyTemplateAsync(db, "Applied twice");
            usedThreeTimes = await SeedHistoryOnlyTemplateAsync(db, "Applied three times");

            await StampAsync(db, usedTwice, target.AddDays(-7), DayType.Workday);
            await StampAsync(db, usedTwice, target.AddDays(-14), DayType.Vacation);
            await StampAsync(db, usedThreeTimes, target.AddDays(-21), DayType.Workday);
            await StampAsync(db, usedThreeTimes, target.AddDays(-28), DayType.Vacation);
            await StampAsync(db, usedThreeTimes, target.AddDays(-35), DayType.SickDay);

            calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, target, UserId, CancellationToken);
        }

        await RefreshTemplateSuggestionViewAsync();

        var suggestions = await GetSuggestionsAsync(calendarId);
        suggestions.Select(s => s.Template.Id).Should().ContainInOrder(usedThreeTimes, usedTwice);
        suggestions.Select(s => s.OccurrenceCount).Should().ContainInOrder(3, 2);
    }

    [Fact(DisplayName = "A day with no matching pattern is 200 and empty, never a 404")]
    public async Task NoPattern_IsAnEmptyList()
    {
        long calendarId;
        await using (var db = CreateDbContext())
            calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, new DateOnly(2028, 4, 4), UserId, CancellationToken);

        await RefreshTemplateSuggestionViewAsync();

        (await GetSuggestionsAsync(calendarId)).Should().BeEmpty();
    }

    [Fact(DisplayName = "Another user's patterns never appear, and their calendar id is a 404")]
    public async Task ForeignPatterns_AreNeitherSuggestedNorReadable()
    {
        var target = new DateOnly(2028, 4, 4);
        var foreignUserId = await ActivityMergeTestSupport.SecondUserAsync(
            Fixture, $"template-suggestions-foreign-{Guid.NewGuid():N}@test.com");

        long foreignCalendarId, ownCalendarId;
        await using (var db = CreateDbContext())
        {
            // Tagged for the very weekday and day type of the own calendar, so neither tier may leak it.
            var foreignTemplateId = await PlanningTestSeedHelper.SeedTaskPlannerDayTemplateAsync(
                db, foreignUserId, "Theirs", CancellationToken, DayType.Workday, [target.AddDays(1).DayOfWeek]);
            await StampAsync(db, foreignTemplateId, target.AddDays(-7), DayType.Workday, foreignUserId);
            await StampAsync(db, foreignTemplateId, target.AddDays(-14), DayType.Vacation, foreignUserId);
            foreignCalendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, target, foreignUserId, CancellationToken);
            ownCalendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, target.AddDays(1), UserId, CancellationToken);
        }

        await RefreshTemplateSuggestionViewAsync();

        (await GetSuggestionsAsync(ownCalendarId)).Should().BeEmpty("the pattern belongs to another user");

        var response = await CreateClient().GetAsync($"api/task-planner-day-template/suggestions/{foreignCalendarId}", CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "their calendar must not resolve at all");
    }

    /// <summary>
    /// <c>CQ-13</c>. The template is deleted through its own endpoint — the scenario a user genuinely produces
    /// — and the view is deliberately <b>not</b> refreshed afterwards, which is the state every read is in
    /// between refresh-job runs.
    /// </summary>
    [Fact(DisplayName = "CQ-13: a view row whose template was just deleted is skipped, not a 500")]
    public async Task DeletedTemplate_IsSkippedUntilTheViewCatchesUp()
    {
        var target = new DateOnly(2028, 4, 4);
        long deletedTemplateId, survivingTemplateId, calendarId;

        await using (var db = CreateDbContext())
        {
            deletedTemplateId = await SeedHistoryOnlyTemplateAsync(db, "Doomed");
            survivingTemplateId = await SeedHistoryOnlyTemplateAsync(db, "Survivor");

            await StampAsync(db, deletedTemplateId, target.AddDays(-7), DayType.Workday);
            await StampAsync(db, deletedTemplateId, target.AddDays(-14), DayType.Vacation);
            await StampAsync(db, survivingTemplateId, target.AddDays(-21), DayType.Workday);
            await StampAsync(db, survivingTemplateId, target.AddDays(-28), DayType.Vacation);

            calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, target, UserId, CancellationToken);
        }

        await RefreshTemplateSuggestionViewAsync();
        (await GetSuggestionsAsync(calendarId)).Should().HaveCount(2, "both patterns are in the view before the delete");

        var delete = await CreateClient().DeleteAsync($"api/task-planner-day-template/{deletedTemplateId}", CancellationToken);
        delete.IsSuccessStatusCode.Should().BeTrue();

        // No refresh: the view still names the deleted template, which is exactly the window CQ-13 is about.
        var suggestions = await GetSuggestionsAsync(calendarId);
        suggestions.Should().ContainSingle().Which.Template.Id.Should().Be(survivingTemplateId,
            "the stale row is skipped and the day still gets its other suggestion");
    }

    // ─── B27: what the user set on the template ──────────────────────────────

    [Fact(DisplayName = "B27: a template scheduled for Tuesdays is suggested on a Tuesday with no history at all")]
    public async Task ScheduledDays_SuggestWithoutHistory()
    {
        var target = new DateOnly(2028, 4, 4); // Tuesday
        long templateId, tuesdayCalendarId, wednesdayCalendarId;

        await using (var db = CreateDbContext())
        {
            templateId = await PlanningTestSeedHelper.SeedTaskPlannerDayTemplateAsync(
                db, UserId, "Tuesdays", CancellationToken, DayType.Special, [DayOfWeek.Tuesday]);
            tuesdayCalendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, target, UserId, CancellationToken);
            wednesdayCalendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, target.AddDays(1), UserId, CancellationToken);
        }

        await RefreshTemplateSuggestionViewAsync();

        var suggestion = (await GetSuggestionsAsync(tuesdayCalendarId)).Should().ContainSingle().Subject;
        suggestion.Template.Id.Should().Be(templateId);
        suggestion.PatternType.Should().Be(2);
        suggestion.PatternLabel.Should().Be(nameof(DayOfWeek.Tuesday));
        suggestion.OccurrenceCount.Should().Be(0);

        (await GetSuggestionsAsync(wednesdayCalendarId)).Should().BeEmpty("the template is scheduled for Tuesdays only");
    }

    [Fact(DisplayName = "B27: a template suggested for Vacation days is suggested on a Vacation day with no history at all")]
    public async Task SuggestedForDayType_SuggestsWithoutHistory()
    {
        var target = new DateOnly(2028, 4, 4);
        long templateId, vacationCalendarId, workdayCalendarId;

        await using (var db = CreateDbContext())
        {
            templateId = await PlanningTestSeedHelper.SeedTaskPlannerDayTemplateAsync(
                db, UserId, "Vacation", CancellationToken, DayType.Vacation);
            vacationCalendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, target, UserId, CancellationToken, DayType.Vacation);
            workdayCalendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, target.AddDays(1), UserId, CancellationToken);
        }

        await RefreshTemplateSuggestionViewAsync();

        var suggestion = (await GetSuggestionsAsync(vacationCalendarId)).Should().ContainSingle().Subject;
        suggestion.Template.Id.Should().Be(templateId);
        suggestion.PatternType.Should().Be(3);
        suggestion.PatternLabel.Should().Be(nameof(DayType.Vacation));
        suggestion.OccurrenceCount.Should().Be(0);

        (await GetSuggestionsAsync(workdayCalendarId)).Should().BeEmpty("a Workday is not a Vacation");
    }

    [Fact(DisplayName = "B27: history ranks first, then templates matching weekday and day type, then those matching one")]
    public async Task HistoryRanksBeforeMetadata_AndDoubleMatchBeforeSingle()
    {
        var target = new DateOnly(2028, 4, 4); // Tuesday, Workday
        long historyId, bothId, dayTypeOnlyId, dayOnlyId, calendarId;

        await using (var db = CreateDbContext())
        {
            historyId = await SeedHistoryOnlyTemplateAsync(db, "Applied on Tuesdays");
            await StampAsync(db, historyId, target.AddDays(-7), DayType.Workday);
            await StampAsync(db, historyId, target.AddDays(-14), DayType.Vacation);

            dayTypeOnlyId = await PlanningTestSeedHelper.SeedTaskPlannerDayTemplateAsync(
                db, UserId, "Workdays", CancellationToken, DayType.Workday);
            dayOnlyId = await PlanningTestSeedHelper.SeedTaskPlannerDayTemplateAsync(
                db, UserId, "Tuesdays", CancellationToken, DayType.Special, [DayOfWeek.Tuesday]);
            // Seeded last, so an id-ordered result would not put it ahead of the single matches by accident.
            bothId = await PlanningTestSeedHelper.SeedTaskPlannerDayTemplateAsync(
                db, UserId, "Workday Tuesdays", CancellationToken, DayType.Workday, [DayOfWeek.Tuesday]);

            calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, target, UserId, CancellationToken);
        }

        await RefreshTemplateSuggestionViewAsync();

        var suggestions = await GetSuggestionsAsync(calendarId);
        suggestions.Should().HaveCount(4);
        suggestions[0].Template.Id.Should().Be(historyId);
        suggestions[0].PatternType.Should().Be(0);
        suggestions[1].Template.Id.Should().Be(bothId);
        suggestions[1].PatternType.Should().Be(2, "a double match reports the weekday");
        suggestions.Skip(2).Select(s => (s.Template.Id, s.PatternType))
            .Should().BeEquivalentTo([(dayTypeOnlyId, 3), (dayOnlyId, 2)]);
    }

    /// <summary>
    /// Two applications on Tuesdays that were both Workdays put the template in the view twice — once per
    /// pattern type — and it is also tagged for Tuesdays and Workdays, so both metadata rules match it too.
    /// Four reasons, one row: the history one.
    /// </summary>
    [Fact(DisplayName = "B27: a template matched by several patterns is suggested once, by its history")]
    public async Task EachTemplate_AppearsOnce()
    {
        var target = new DateOnly(2028, 4, 4);
        long templateId, calendarId;

        await using (var db = CreateDbContext())
        {
            templateId = await PlanningTestSeedHelper.SeedTaskPlannerDayTemplateAsync(
                db, UserId, "Everything matches", CancellationToken, DayType.Workday, [DayOfWeek.Tuesday]);
            await StampAsync(db, templateId, target.AddDays(-7), DayType.Workday);
            await StampAsync(db, templateId, target.AddDays(-14), DayType.Workday);
            calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, target, UserId, CancellationToken);
        }

        await RefreshTemplateSuggestionViewAsync();

        var suggestion = (await GetSuggestionsAsync(calendarId)).Should().ContainSingle().Subject;
        suggestion.Template.Id.Should().Be(templateId);
        suggestion.PatternType.Should().Be(0, "history outranks metadata, and on a tie the weekday pattern wins");
        suggestion.OccurrenceCount.Should().Be(2);
    }

    [Fact(DisplayName = "B27: a switched-off template is never suggested, by history or by metadata")]
    public async Task InactiveTemplate_IsNeverSuggested()
    {
        var target = new DateOnly(2028, 4, 4);
        long calendarId;

        await using (var db = CreateDbContext())
        {
            var inactiveId = await PlanningTestSeedHelper.SeedTaskPlannerDayTemplateAsync(
                db, UserId, "Switched off", CancellationToken, DayType.Workday, [DayOfWeek.Tuesday], isActive: false);
            await StampAsync(db, inactiveId, target.AddDays(-7), DayType.Workday);
            await StampAsync(db, inactiveId, target.AddDays(-14), DayType.Workday);
            calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, target, UserId, CancellationToken);
        }

        await RefreshTemplateSuggestionViewAsync();

        (await GetSuggestionsAsync(calendarId)).Should().BeEmpty();
    }

    // ─── FO1: away days ──────────────────────────────────────────────────────

    /// <summary>
    /// "Office" has been applied on three Tuesdays and "Beach" on two vacation days. On a Tuesday the user marked
    /// as vacation, every day-type match — history first, then metadata — ranks above every weekday match, even
    /// though the weekday history has more occurrences. Weekday matches are demoted, not dropped.
    /// </summary>
    [Fact(DisplayName = "FO1: on an away day, day-type suggestions rank above weekday ones")]
    public async Task AwayDay_RanksDayTypeAboveWeekday()
    {
        var target = new DateOnly(2028, 4, 4); // Tuesday
        long officeId, beachId, vacationTagId, tuesdayTagId, calendarId;

        await using (var db = CreateDbContext())
        {
            officeId = await SeedHistoryOnlyTemplateAsync(db, "Office");
            await StampAsync(db, officeId, target.AddDays(-7), DayType.Workday);
            await StampAsync(db, officeId, target.AddDays(-14), DayType.Workday);
            await StampAsync(db, officeId, target.AddDays(-21), DayType.Workday);

            beachId = await SeedHistoryOnlyTemplateAsync(db, "Beach");
            // Two different weekdays, neither a Tuesday, so Beach is a day-type pattern only.
            await StampAsync(db, beachId, target.AddDays(-3), DayType.Vacation);
            await StampAsync(db, beachId, target.AddDays(-9), DayType.Vacation);

            vacationTagId = await PlanningTestSeedHelper.SeedTaskPlannerDayTemplateAsync(
                db, UserId, "Lazy day", CancellationToken, DayType.Vacation);
            tuesdayTagId = await PlanningTestSeedHelper.SeedTaskPlannerDayTemplateAsync(
                db, UserId, "Tuesdays", CancellationToken, DayType.Special, [DayOfWeek.Tuesday]);

            calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, target, UserId, CancellationToken, DayType.Vacation);
        }

        await RefreshTemplateSuggestionViewAsync();

        var suggestions = await GetSuggestionsAsync(calendarId);
        suggestions.Select(s => (s.Template.Id, s.PatternType)).Should().Equal(
            (beachId, 1), (vacationTagId, 3), (officeId, 0), (tuesdayTagId, 2));
    }

    // ─── C3: the day's location ──────────────────────────────────────────────

    /// <summary>
    /// A Wednesday marked Office. "Home office" is scheduled for Wednesdays and tagged Workday, the double match
    /// that led the settings tier before C3; "Office day" matches only the day type and "Office desk" nothing but the
    /// location. Both location matches now rank ahead of it, reported as type 5, and history still comes first. The
    /// same templates a week later, on a Wednesday with no location, rank as they did before C3: a null location
    /// matches nothing, so "Office desk" is not suggested at all.
    /// </summary>
    [Fact(DisplayName = "C3: a location match ranks first among the template settings, after history; no location matches nothing")]
    public async Task Location_RanksFirstAmongMetadata_AndANullLocationMatchesNothing()
    {
        var target = new DateOnly(2028, 4, 5); // Wednesday, Workday
        long historyId, homeOfficeId, officeDayId, officeDeskId, officeCalendarId, plainCalendarId;

        await using (var db = CreateDbContext())
        {
            historyId = await SeedHistoryOnlyTemplateAsync(db, "Applied on Wednesdays");
            await StampAsync(db, historyId, target.AddDays(-7), DayType.Workday);
            await StampAsync(db, historyId, target.AddDays(-14), DayType.Vacation);

            officeDayId = await PlanningTestSeedHelper.SeedTaskPlannerDayTemplateAsync(
                db, UserId, "Office day", CancellationToken, DayType.Workday, [DayOfWeek.Tuesday, DayOfWeek.Thursday],
                suggestedLocation: Location.Office);
            officeDeskId = await PlanningTestSeedHelper.SeedTaskPlannerDayTemplateAsync(
                db, UserId, "Office desk", CancellationToken, DayType.Special, suggestedLocation: Location.Office);
            await PlanningTestSeedHelper.SeedTaskPlannerDayTemplateAsync(
                db, UserId, "Switched-off office", CancellationToken, DayType.Special, isActive: false,
                suggestedLocation: Location.Office);
            // Seeded last and used most, so neither id order nor usage puts the location matches ahead by accident.
            homeOfficeId = await PlanningTestSeedHelper.SeedTaskPlannerDayTemplateAsync(
                db, UserId, "Home office", CancellationToken, DayType.Workday, [DayOfWeek.Wednesday],
                suggestedLocation: Location.Home);
            var homeOffice = await db.Set<TaskPlannerDayTemplate>().SingleAsync(t => t.Id == homeOfficeId, CancellationToken);
            homeOffice.UsageCount = 9;
            await db.SaveChangesAsync(CancellationToken);

            officeCalendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, target, UserId, CancellationToken);
            await SetLocationAsync(db, officeCalendarId, Location.Office);
            plainCalendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, target.AddDays(7), UserId, CancellationToken);
        }

        await RefreshTemplateSuggestionViewAsync();

        var atTheOffice = await GetSuggestionsAsync(officeCalendarId);
        atTheOffice.Select(s => (s.Template.Id, s.PatternType)).Should().Equal(
            (historyId, 0), (officeDayId, 5), (officeDeskId, 5), (homeOfficeId, 2));
        atTheOffice.Where(s => s.PatternType == 5).Should().OnlyContain(s => s.PatternLabel == nameof(Location.Office) && s.OccurrenceCount == 0);

        var withNoLocation = await GetSuggestionsAsync(plainCalendarId);
        withNoLocation.Select(s => (s.Template.Id, s.PatternType)).Should().Equal(
            (historyId, 0), (homeOfficeId, 2), (officeDayId, 3));
    }

    /// <summary>
    /// A vacation day in Travel: the location sits with the day-type tier, ahead of it, and both still rank above
    /// every weekday match (FO1).
    /// </summary>
    [Fact(DisplayName = "C3: on an away day a location match ranks with the day-type matches, ahead of them")]
    public async Task AwayDay_RanksLocationWithDayType_AboveWeekday()
    {
        var target = new DateOnly(2028, 4, 4); // Tuesday
        long lazyDayId, tripId, tuesdaysId, calendarId;

        await using (var db = CreateDbContext())
        {
            lazyDayId = await PlanningTestSeedHelper.SeedTaskPlannerDayTemplateAsync(
                db, UserId, "Lazy day", CancellationToken, DayType.Vacation);
            tripId = await PlanningTestSeedHelper.SeedTaskPlannerDayTemplateAsync(
                db, UserId, "On the road", CancellationToken, DayType.Special, suggestedLocation: Location.Travel);
            tuesdaysId = await PlanningTestSeedHelper.SeedTaskPlannerDayTemplateAsync(
                db, UserId, "Tuesdays", CancellationToken, DayType.Special, [DayOfWeek.Tuesday]);

            calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, target, UserId, CancellationToken, DayType.Vacation);
            await SetLocationAsync(db, calendarId, Location.Travel);
        }

        await RefreshTemplateSuggestionViewAsync();

        var suggestions = await GetSuggestionsAsync(calendarId);
        suggestions.Select(s => (s.Template.Id, s.PatternType)).Should().Equal(
            (tripId, 5), (lazyDayId, 3), (tuesdaysId, 2));
        suggestions[0].PatternLabel.Should().Be(nameof(Location.Travel));
    }

    // ─── helpers ─────────────────────────────────────────────────────────────

    private async Task SetLocationAsync(DbContext db, long calendarId, Location location)
    {
        var calendar = await db.Set<Calendar>().IgnoreQueryFilters().SingleAsync(c => c.Id == calendarId, CancellationToken);
        calendar.Location = location;
        await db.SaveChangesAsync(CancellationToken);
    }

    /// <summary>
    /// A template whose own metadata matches none of this file's target days — no calendar here is
    /// <c>Special</c> and <c>ScheduledDays</c> is empty — so it can only be suggested through history.
    /// </summary>
    private Task<long> SeedHistoryOnlyTemplateAsync(DbContext db, string name) =>
        PlanningTestSeedHelper.SeedTaskPlannerDayTemplateAsync(db, UserId, name, CancellationToken, DayType.Special);

    /// <summary>
    /// A day the template was applied to. Only <c>applied_template_id</c> matters to the view, so the stamp is
    /// written directly rather than by driving the apply endpoint — which would need a whole template body and
    /// would make the fixture about applying rather than about suggesting.
    /// </summary>
    private async Task StampAsync(DbContext db, long templateId, DateOnly date, DayType dayType, long userId = UserId)
    {
        var calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, date, userId, CancellationToken, dayType);
        var calendar = await db.Set<Calendar>().IgnoreQueryFilters().SingleAsync(c => c.Id == calendarId, CancellationToken);
        calendar.AppliedTemplateId = templateId;
        await db.SaveChangesAsync(CancellationToken);
    }

    /// <summary>
    /// Same reasoning as <c>RepeatingPlannerTaskSuggestionsTests.RefreshSuggestionViewsAsync</c>: the refresh
    /// interceptor is wired only on the host's <c>AppDbContext</c> pipeline, so seeding through the bare
    /// fixture context never marks the queue dirty and the job handler would drain an empty set.
    /// </summary>
    private async Task RefreshTemplateSuggestionViewAsync()
    {
        await using var db = CreateDbContext();
#pragma warning disable EF1002
        await db.Database.ExecuteSqlRawAsync(
            $"REFRESH MATERIALIZED VIEW CONCURRENTLY {ModuleSchemas.Planning}.mv_template_suggestion_pattern", CancellationToken);
#pragma warning restore EF1002
    }

    private async Task<List<TemplateSuggestionResponse>> GetSuggestionsAsync(long calendarId)
    {
        var response = await CreateClient().GetAsync($"api/task-planner-day-template/suggestions/{calendarId}", CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<List<TemplateSuggestionResponse>>(JsonOpts, CancellationToken))!;
    }
}
