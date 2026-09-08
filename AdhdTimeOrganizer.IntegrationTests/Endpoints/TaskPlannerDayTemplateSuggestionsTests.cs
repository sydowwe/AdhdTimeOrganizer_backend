using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.Core.domain.model.@enum;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Planning.application.dto.response.suggestion;
using AdhdTimeOrganizer.Planning.domain.model.entity;
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
            templateId = await PlanningTestSeedHelper.SeedTaskPlannerDayTemplateAsync(db, UserId, "Tuesday routine", CancellationToken);
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
            templateId = await PlanningTestSeedHelper.SeedTaskPlannerDayTemplateAsync(db, UserId, "Sunday routine", CancellationToken);
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
            templateId = await PlanningTestSeedHelper.SeedTaskPlannerDayTemplateAsync(db, UserId, "Holiday routine", CancellationToken);
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
            usedTwice = await PlanningTestSeedHelper.SeedTaskPlannerDayTemplateAsync(db, UserId, "Applied twice", CancellationToken);
            usedThreeTimes = await PlanningTestSeedHelper.SeedTaskPlannerDayTemplateAsync(db, UserId, "Applied three times", CancellationToken);

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
            var foreignTemplateId = await PlanningTestSeedHelper.SeedTaskPlannerDayTemplateAsync(db, foreignUserId, "Theirs", CancellationToken);
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
            deletedTemplateId = await PlanningTestSeedHelper.SeedTaskPlannerDayTemplateAsync(db, UserId, "Doomed", CancellationToken);
            survivingTemplateId = await PlanningTestSeedHelper.SeedTaskPlannerDayTemplateAsync(db, UserId, "Survivor", CancellationToken);

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

    // ─── helpers ─────────────────────────────────────────────────────────────

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
