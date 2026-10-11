using System.Net;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.domain.extServiceContract.googleCalendar;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.model.entity.reminder;
using FastEndpoints;
using Google;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.error;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.result;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.calendar.command;

/// <param name="SyncedCount">Events inserted or updated on Google — the tasks now showing there.</param>
/// <param name="RemovedCount">Events deleted because their task was skipped or made background since the last sync.</param>
public record SyncCalendarToGoogleResponse(int SyncedCount, int RemovedCount);

public class SyncCalendarToGoogleEndpoint(
    AppDbContext dbContext,
    UserManager<User> userManager,
    IGoogleCalendarService googleCalendarService)
    : EndpointWithoutRequest<SyncCalendarToGoogleResponse>
{
    public override void Configure()
    {
        Post("calendar/{id:long:required}/sync-to-google");

        Summary(s => { s.Summary = "Sync a calendar day's planner tasks to Google Calendar"; });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            await Send.UnauthorizedAsync(ct);
            return;
        }

        if (user.GoogleCalendarRefreshToken is null)
        {
            AddError("Google Calendar is not connected. Please connect it first via /user/google-calendar/connect.");
            await Send.ErrorsAsync(400, ct);
            return;
        }

        var calendarId = Route<long>("id");
        var calendar = await dbContext.Calendars
            .Include(c => c.Tasks)
            .ThenInclude(t => t.Activity)
            .FirstOrDefaultAsync(c => c.Id == calendarId && c.UserId == User.GetId(), ct);

        if (calendar is null)
        {
            AddError("Calendar not found.");
            await Send.ErrorsAsync(404, ct);
            return;
        }

        // The tasks the portal itself will remind about — a reminder the user wrote or an automatic one (audit N1).
        var taskIds = calendar.Tasks.Select(t => t.Id).ToList();
        var remindedTaskIds = await dbContext.Set<Reminder>()
            .Where(r => r.PlannerTaskId != null && taskIds.Contains(r.PlannerTaskId.Value))
            .Select(r => r.PlannerTaskId!.Value)
            .ToHashSetAsync(ct);

        using var calendarService = googleCalendarService.GetCalendarService(user.GoogleCalendarRefreshToken);
        var tzOffset = user.Timezone.GetUtcOffset(DateTime.UtcNow);
        var syncedCount = 0;
        var removedCount = 0;

        try
        {
            foreach (var task in calendar.Tasks)
            {
                // A skipped task, or a background one, is not a commitment to show on the user's phone — and
                // an event left there from an earlier sync would keep alerting. Remove it and forget its id,
                // so un-skipping the task later inserts a fresh event.
                if (!IsPushable(task))
                {
                    if (task.GoogleEventId is not null)
                    {
                        await DeleteEventAsync(calendarService, task.GoogleEventId, ct);
                        task.GoogleEventId = null;
                        removedCount++;
                    }

                    continue;
                }

                var startDto = new DateTimeOffset(
                    calendar.Date.Year, calendar.Date.Month, calendar.Date.Day,
                    task.StartTime.Hour, task.StartTime.Minute, 0, tzOffset);

                var endDto = new DateTimeOffset(
                    calendar.Date.Year, calendar.Date.Month, calendar.Date.Day,
                    task.EndTime.Hour, task.EndTime.Minute, 0, tzOffset);

                var googleEvent = new Event
                {
                    Summary = task.Activity.Name,
                    Description = task.Notes,
                    Location = task.Location,
                    Start = new EventDateTime { DateTimeDateTimeOffset = startDto, TimeZone = user.Timezone.Id },
                    End = new EventDateTime { DateTimeDateTimeOffset = endDto, TimeZone = user.Timezone.Id },
                    Reminders = GoogleReminders(remindedTaskIds.Contains(task.Id))
                };

                if (task.GoogleEventId is null)
                {
                    var created = await calendarService.Events.Insert(googleEvent, "primary").ExecuteAsync(ct);
                    task.GoogleEventId = created.Id;
                }
                else
                {
                    await calendarService.Events.Update(googleEvent, "primary", task.GoogleEventId).ExecuteAsync(ct);
                }

                syncedCount++;
            }
        }
        catch (GoogleApiException e) when (e.HttpStatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            AddError("Google Calendar access was revoked or expired. Please reconnect it via /user/google-calendar/connect.");
            await Send.ErrorsAsync(this.ErrorStatus(ResultErrorType.Conflict), ct);
            return;
        }
        catch (GoogleApiException)
        {
            AddError("Google Calendar sync failed. Please try again later.");
            await Send.ErrorsAsync(502, ct);
            return;
        }

        await dbContext.SaveChangesAsync(ct);

        await Send.OkAsync(new SyncCalendarToGoogleResponse(syncedCount, removedCount), ct);
    }

    private static bool IsPushable(PlannerTask task) =>
        task.Status != PlannerTaskStatus.Cancelled && !task.IsBackground;

    /// <summary>
    /// Google's own popups for a synced block. A task the portal already reminds about gets none — without this
    /// Google applies the calendar's default alert too, and a phone with Google Calendar rings twice per block. The
    /// portal's reminder is the one to keep: it respects quiet hours, the away hold and snooze, and Google's does
    /// not. A task without a portal reminder keeps Google's defaults, and says so explicitly, because the sync
    /// <i>updates</i> existing events: a block whose portal reminder was deleted gets its Google alert back.
    /// </summary>
    private static Event.RemindersData GoogleReminders(bool portalReminds) =>
        portalReminds
            ? new Event.RemindersData { UseDefault = false, Overrides = [] }
            : new Event.RemindersData { UseDefault = true };

    // Already gone on Google's side (the user deleted it there) is the outcome we wanted, not a failure.
    private static async Task DeleteEventAsync(CalendarService calendarService, string eventId, CancellationToken ct)
    {
        try
        {
            await calendarService.Events.Delete("primary", eventId).ExecuteAsync(ct);
        }
        catch (GoogleApiException e) when (e.HttpStatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
        }
    }
}