using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.Core.domain.model.@enum;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// Fixtures shared by the activity-planning endpoint tests (calendar / planner-task / repeating-planner-task /
/// task-importance / task-planner-day-template / template-planner-task / planner-settings). Every planner
/// entity ultimately needs an <see cref="Activity"/> (which needs an <see cref="ActivityRole"/>), so building
/// one is factored out here rather than repeated in every test file.
/// </summary>
public static class PlanningTestSeedHelper
{
    public const long TestUserId = FakeLoggedUserService.TestUserId;

    /// <summary>
    /// Moves <paramref name="userId"/> onto a real system time zone in which it is a <b>different calendar
    /// date</b> than it is in UTC right now, and returns that zone — the shape every "today is the user's
    /// today, not the server's" pin needs (<c>CQ-5</c>).
    ///
    /// <para>Chosen at run time rather than hard-coded, because no fixed zone is on the other side of
    /// midnight at every hour of the day: the search below always finds one (system zones span UTC-12 to
    /// UTC+14, so at any instant some zone is a day ahead or a day behind), which makes the boundary case
    /// constructible whenever the suite happens to run instead of only in the hours before UTC midnight.
    /// It must be a system zone: <c>User.Timezone</c> is persisted as its id and read back through
    /// <c>TimeZoneInfo.FindSystemTimeZoneById</c>, so a custom-offset zone would not round-trip.</para>
    ///
    /// <para>Safe to mutate the shared fixture user: Respawn deletes the row between tests and
    /// <c>AppDbContextFixture.SeedFixtureAsync</c> re-creates it on <c>TimeZoneInfo.Utc</c>.</para>
    /// </summary>
    public static async Task<TimeZoneInfo> UseZoneOffsetFromUtcTodayAsync(DbContext db, long userId = TestUserId, CancellationToken ct = default)
    {
        var utcNow = DateTime.UtcNow;
        var utcToday = DateOnly.FromDateTime(utcNow);

        var zone = TimeZoneInfo.GetSystemTimeZones()
            .FirstOrDefault(z => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utcNow, z)) != utcToday)
            ?? throw new InvalidOperationException("No system time zone is on a different calendar date than UTC right now.");

        var user = await db.Set<User>().IgnoreQueryFilters().SingleAsync(u => u.Id == userId, ct);
        user.Timezone = zone;
        await db.SaveChangesAsync(ct);
        return zone;
    }

    /// <summary>The date it is for a user in <paramref name="zone"/> — what the write paths must resolve.</summary>
    public static DateOnly TodayIn(TimeZoneInfo zone) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone));

    public static async Task<long> SeedActivityAsync(DbContext db, string name, long userId = TestUserId, CancellationToken ct = default)
    {
        var role = new ActivityRole { UserId = userId, Name = $"{name}-role-{Guid.NewGuid():N}", Color = "#123456" };
        db.Set<ActivityRole>().Add(role);
        await db.SaveChangesAsync(ct);

        var activity = new Activity { UserId = userId, Name = name, RoleId = role.Id };
        db.Set<Activity>().Add(activity);
        await db.SaveChangesAsync(ct);
        return activity.Id;
    }

    // dayType trails the cancellation token deliberately: several callers already pass ct positionally as the
    // fourth argument, so a new parameter ahead of it would rebind their token to the day type.
    public static async Task<long> SeedCalendarAsync(
        DbContext db, DateOnly date, long userId = TestUserId, CancellationToken ct = default, DayType dayType = DayType.Workday)
    {
        var existing = await db.Set<Calendar>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.UserId == userId && c.Date == date, ct);
        if (existing != null)
            return existing.Id;

        var calendar = new Calendar
        {
            UserId = userId,
            Date = date,
            DayType = dayType,
            WakeUpTime = new TimeOnly(7, 0),
            BedTime = new TimeOnly(23, 0)
        };
        db.Set<Calendar>().Add(calendar);
        await db.SaveChangesAsync(ct);
        return calendar.Id;
    }

    public static async Task<long> SeedPlannerTaskAsync(
        DbContext db,
        long activityId,
        long calendarId,
        TimeOnly start,
        TimeOnly end,
        long userId = TestUserId,
        PlannerTaskStatus status = PlannerTaskStatus.NotStarted,
        long? importanceId = null,
        CancellationToken ct = default)
    {
        var task = new PlannerTask
        {
            UserId = userId,
            ActivityId = activityId,
            CalendarId = calendarId,
            StartTime = start,
            EndTime = end,
            IsBackground = false,
            Status = status,
            ImportanceId = importanceId
        };
        db.Set<PlannerTask>().Add(task);
        await db.SaveChangesAsync(ct);
        return task.Id;
    }

    public static async Task<long> SeedTaskImportanceAsync(
        DbContext db, int importance, long userId = TestUserId, string? text = null, CancellationToken ct = default)
    {
        var entity = new TaskImportance
        {
            UserId = userId,
            Text = text ?? $"Importance {importance}-{Guid.NewGuid():N}",
            Color = "#abcdef",
            Importance = importance
        };
        db.Set<TaskImportance>().Add(entity);
        await db.SaveChangesAsync(ct);
        return entity.Id;
    }

    public static async Task<long> SeedTaskPlannerDayTemplateAsync(
        DbContext db, long userId = TestUserId, string? name = null, CancellationToken ct = default,
        DayType suggestedForDayType = DayType.Workday, List<DayOfWeek>? scheduledDays = null, bool isActive = true,
        Location? suggestedLocation = null)
    {
        var template = new TaskPlannerDayTemplate
        {
            UserId = userId,
            Name = name ?? $"Template {Guid.NewGuid():N}",
            IsActive = isActive,
            SuggestedForDayType = suggestedForDayType,
            ScheduledDays = scheduledDays ?? [],
            SuggestedLocation = suggestedLocation
        };
        db.Set<TaskPlannerDayTemplate>().Add(template);
        await db.SaveChangesAsync(ct);
        return template.Id;
    }

    public static async Task<long> SeedTemplatePlannerTaskAsync(
        DbContext db, long templateId, long activityId, TimeOnly start, TimeOnly end,
        long userId = TestUserId, CancellationToken ct = default)
    {
        var task = new TemplatePlannerTask
        {
            UserId = userId,
            TemplateId = templateId,
            ActivityId = activityId,
            StartTime = start,
            EndTime = end,
            IsBackground = false
        };
        db.Set<TemplatePlannerTask>().Add(task);
        await db.SaveChangesAsync(ct);
        return task.Id;
    }

    public static async Task<long> SeedRepeatingPlannerTaskAsync(
        DbContext db, long activityId, long userId = TestUserId,
        RecurrenceType recurrenceType = RecurrenceType.DayOfWeek,
        TimeOnly? start = null, TimeOnly? end = null, bool isActive = true, CancellationToken ct = default)
    {
        var task = new RepeatingPlannerTask
        {
            UserId = userId,
            ActivityId = activityId,
            StartTime = start ?? new TimeOnly(9, 0),
            EndTime = end ?? new TimeOnly(10, 0),
            IsBackground = false,
            IsActive = isActive,
            RecurrenceType = recurrenceType
        };
        db.Set<RepeatingPlannerTask>().Add(task);
        await db.SaveChangesAsync(ct);
        return task.Id;
    }
}
