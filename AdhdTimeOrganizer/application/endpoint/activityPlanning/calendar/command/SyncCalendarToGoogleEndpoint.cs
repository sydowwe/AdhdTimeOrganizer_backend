using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.application.helper;
using AdhdTimeOrganizer.domain.model.entity.user;
using AdhdTimeOrganizer.domain.extServiceContract.googleCalendar;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Google.Apis.Calendar.v3.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.calendar.command;

public record SyncCalendarToGoogleResponse(int SyncedCount);

public class SyncCalendarToGoogleEndpoint(
    AppDbContext dbContext,
    UserManager<User> userManager,
    IGoogleCalendarService googleCalendarService)
    : EndpointWithoutRequest<SyncCalendarToGoogleResponse>
{
    public virtual string[] AllowedRoles() => EndpointHelper.GetUserOrHigherRoles();

    public override void Configure()
    {
        Post("calendar/{id:long}/sync-to-google");
        Roles(AllowedRoles());
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
            await Send.NotFoundAsync(ct);
            return;
        }

        var calendarService = googleCalendarService.GetCalendarService(user.GoogleCalendarRefreshToken);
        var tzOffset = user.Timezone.GetUtcOffset(DateTime.UtcNow);
        var syncedCount = 0;

        foreach (var task in calendar.Tasks)
        {
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
                End = new EventDateTime { DateTimeDateTimeOffset = endDto, TimeZone = user.Timezone.Id }
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

        await dbContext.SaveChangesAsync(ct);

        await Send.OkAsync(new SyncCalendarToGoogleResponse(syncedCount), ct);
    }
}
