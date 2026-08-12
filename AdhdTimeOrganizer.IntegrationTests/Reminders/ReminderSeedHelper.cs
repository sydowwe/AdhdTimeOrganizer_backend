using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using AdhdTimeOrganizer.Core.domain.model.@enum;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.model.entity.reminder;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;

namespace AdhdTimeOrganizer.IntegrationTests.Reminders;

/// <summary>
/// Fixtures the reminder tests share. A planner task is the awkward one — it needs an Activity (which needs
/// an ActivityRole) and a Calendar before it can exist at all — so building one is factored out here rather
/// than repeated in every test that only cares about the reminder hanging off it.
/// </summary>
public static class ReminderSeedHelper
{
    /// <summary>A second real user row, so the IDOR tests have someone to be denied as.</summary>
    public const long OtherUserId = FakeLoggedUserService.TestUserId + 1;

    public static async Task<User> EnsureOtherUserAsync(DbContext db, CancellationToken ct)
    {
        var users = db.Set<User>();
        if (await users.FindAsync([OtherUserId], ct) is { } existing)
            return existing;

        const string email = "other@test.com";
        var user = new User
        {
            Id = OtherUserId,
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N"),
            Timezone = TimeZoneInfo.Utc
        };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, "Test@1234!");
        users.Add(user);
        await db.SaveChangesAsync(ct);
        return user;
    }

    /// <summary>A planner task on <paramref name="date"/> starting at <paramref name="startTime"/>, with its Activity/Role/Calendar.</summary>
    public static async Task<PlannerTask> SeedPlannerTaskAsync(
        DbContext db,
        long userId,
        DateOnly date,
        TimeOnly startTime,
        PlannerTaskStatus status = PlannerTaskStatus.NotStarted,
        CancellationToken ct = default)
    {
        var role = new ActivityRole { UserId = userId, Name = $"Role {Guid.NewGuid():N}", Color = "#123456" };
        db.Set<ActivityRole>().Add(role);
        await db.SaveChangesAsync(ct);

        var activity = new Activity { UserId = userId, Name = $"Activity {Guid.NewGuid():N}", RoleId = role.Id };
        db.Set<Activity>().Add(activity);

        var calendar = new Calendar
        {
            UserId = userId,
            Date = date,
            DayType = DayType.Workday,
            WakeUpTime = new TimeOnly(7, 0),
            BedTime = new TimeOnly(23, 0)
        };
        db.Set<Calendar>().Add(calendar);
        await db.SaveChangesAsync(ct);

        var task = new PlannerTask
        {
            UserId = userId,
            ActivityId = activity.Id,
            CalendarId = calendar.Id,
            StartTime = startTime,
            EndTime = startTime.AddHours(1),
            IsBackground = false,
            Status = status
        };
        db.Set<PlannerTask>().Add(task);
        await db.SaveChangesAsync(ct);
        return task;
    }

    public static async Task<Reminder> SeedReminderAsync(
        DbContext db,
        long userId,
        DateTime remindAt,
        string title = "Call the dentist",
        long? plannerTaskId = null,
        ReminderRecurrence? recurrence = null,
        CancellationToken ct = default)
    {
        var reminder = new Reminder
        {
            UserId = userId,
            Title = title,
            RemindAt = DateTime.SpecifyKind(remindAt, DateTimeKind.Utc),
            PlannerTaskId = plannerTaskId,
            Recurrence = recurrence
        };
        db.Set<Reminder>().Add(reminder);
        await db.SaveChangesAsync(ct);
        return reminder;
    }
}