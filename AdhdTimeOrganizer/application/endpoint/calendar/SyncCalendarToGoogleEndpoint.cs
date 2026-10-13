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
using Google.Apis.Util;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.error;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.helper;
using Sydowwe.Framework.domain.result;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.calendar.command;

/// <param name="SyncedCount">Events inserted or updated on Google — the tasks now showing there.</param>
/// <param name="RemovedCount">
/// Events deleted because their task was skipped, made background, deleted or moved to another day since the last sync.
/// </param>
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
        var zone = user.Timezone;
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

                // Each instant through the user's zone on the calendar's own date. Taking today's offset instead put
                // every day across a DST switch an hour off. An end before the start belongs to a row that predates
                // the ordering rule and wrapped past midnight (DayPacking.Occupancy reads it the same way).
                var start = WallClockZone.ToUtc(calendar.Date, task.StartTime, zone);
                var end = WallClockZone.ToUtc(task.EndTime < task.StartTime ? calendar.Date.AddDays(1) : calendar.Date, task.EndTime, zone);

                var googleEvent = new Event
                {
                    Summary = task.Activity.Name,
                    Description = task.Notes,
                    Location = task.Location,
                    Start = new EventDateTime { DateTimeDateTimeOffset = new DateTimeOffset(start), TimeZone = zone.Id },
                    End = new EventDateTime { DateTimeDateTimeOffset = new DateTimeOffset(end), TimeZone = zone.Id },
                    Reminders = GoogleReminders(remindedTaskIds.Contains(task.Id)),
                    ExtendedProperties = Tag(task.Id),
                    // An update restores an event Google holds as cancelled rather than leaving it hidden.
                    Status = "confirmed"
                };

                task.GoogleEventId = await UpsertEventAsync(calendarService, googleEvent, task.GoogleEventId, ct);
                syncedCount++;
            }

            // Last, so the events just inserted are listed with their ids already recorded and kept.
            var dayStart = WallClockZone.ToUtc(calendar.Date, TimeOnly.MinValue, zone);
            var dayEnd = WallClockZone.ToUtc(calendar.Date.AddDays(1), TimeOnly.MinValue, zone);
            var kept = calendar.Tasks.Where(IsPushable).Select(t => t.GoogleEventId).OfType<string>().ToHashSet();
            removedCount += await RemoveOrphanedEventsAsync(calendarService, user.Id, calendar.Id, kept, dayStart, dayEnd, ct);
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

    /// <summary>
    /// Marks the events this sync owns, so a day's sync can find the ones whose block is gone. The list query filters
    /// on the marker, so an event the user made in Google is never read, let alone deleted.
    /// </summary>
    private const string MarkerKey = "adhdTimeOrganizer";

    private const string MarkerValue = "plannerTask";
    private const string TaskIdKey = "plannerTaskId";

    private static bool IsPushable(PlannerTask task) =>
        task.Status != PlannerTaskStatus.Cancelled && !task.IsBackground;

    private static Event.ExtendedPropertiesData Tag(long taskId) => new()
    {
        Private__ = new Dictionary<string, string> { [MarkerKey] = MarkerValue, [TaskIdKey] = taskId.ToString() }
    };

    /// <summary>
    /// Inserts, or updates the event the task already has. An event Google no longer has — deleted there by hand, or by
    /// the orphan sweep of the day the task moved off — is inserted afresh rather than failing the whole sync.
    /// </summary>
    private static async Task<string> UpsertEventAsync(CalendarService calendarService, Event googleEvent, string? eventId, CancellationToken ct)
    {
        if (eventId is not null)
        {
            try
            {
                await calendarService.Events.Update(googleEvent, "primary", eventId).ExecuteAsync(ct);
                return eventId;
            }
            catch (GoogleApiException e) when (e.HttpStatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
            {
            }
        }

        var created = await calendarService.Events.Insert(googleEvent, "primary").ExecuteAsync(ct);
        return created.Id;
    }

    /// <summary>
    /// Deletes this sync's events on the day that no pushable task of the day owns any more: a block deleted in the
    /// app, or by a cascade (an activity's delete, a template replacing the day), which no code path reports to Google.
    /// Found by listing the day on Google rather than by remembering deletions, so every delete path is covered,
    /// including the ones that bypass EF. Events synced before the marker existed carry none and are left alone — an
    /// untagged event cannot be told apart from one the user made.
    /// <para>
    /// A task elsewhere still pointing at a removed event (moved to another day and not synced there yet) forgets it, so
    /// its own day's sync inserts a fresh event instead of updating a deleted one. This day's own tasks are excluded:
    /// they are tracked and saved by the caller, and a set-based write under them would fail that save on its
    /// concurrency token.
    /// </para>
    /// </summary>
    private async Task<int> RemoveOrphanedEventsAsync(
        CalendarService calendarService, long userId, long calendarId, IReadOnlySet<string> kept,
        DateTime dayStartUtc, DateTime dayEndUtc, CancellationToken ct)
    {
        var orphans = new List<string>();
        string? pageToken = null;
        do
        {
            var request = calendarService.Events.List("primary");
            request.TimeMinDateTimeOffset = new DateTimeOffset(dayStartUtc);
            request.TimeMaxDateTimeOffset = new DateTimeOffset(dayEndUtc);
            request.PrivateExtendedProperty = new Repeatable<string>([$"{MarkerKey}={MarkerValue}"]);
            request.PageToken = pageToken;

            var page = await request.ExecuteAsync(ct);
            orphans.AddRange((page.Items ?? []).Select(e => e.Id).OfType<string>().Where(id => !kept.Contains(id)));
            pageToken = page.NextPageToken;
        } while (pageToken is not null);

        foreach (var eventId in orphans)
            await DeleteEventAsync(calendarService, eventId, ct);

        if (orphans.Count > 0)
            await dbContext.PlannerTasks
                .Where(t => t.UserId == userId && t.CalendarId != calendarId
                            && t.GoogleEventId != null && orphans.Contains(t.GoogleEventId))
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.GoogleEventId, (string?)null), ct);

        return orphans.Count;
    }

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