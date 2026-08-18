using System.Text;
using System.Text.Json;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.application.endpoint.user.read;

public class GetUserDataExportEndpoint(AppDbContext dbContext, IDistributedCache cache) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("/user/data-export");
        Summary(s =>
        {
            s.Summary = "Download all user data as a JSON file (max 1/min)";
            s.Description = "Exports all user data as a JSON file. Rate-limited to 1 request per minute.";
            s.Response(200, "Success");
            s.Response(429, "Too many requests");
            s.Response(401, "Unauthorized");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var userId = User.GetId();

        var throttleKey = $"throttle:data-export:{userId}";
        if (await cache.GetStringAsync(throttleKey, ct) is not null)
        {
            AddError("Please wait 1 minute before requesting another export.");
            await Send.ErrorsAsync(429, ct);
            return;
        }

        await cache.SetStringAsync(throttleKey, "1", new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1)
        }, ct);

        var user = await dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user is null)
        {
            await Send.UnauthorizedAsync(ct);
            return;
        }

        var plannerTasks = await dbContext.PlannerTasks
            .AsNoTracking()
            .Where(t => t.UserId == userId)
            .Select(t => new { t.Id, t.StartTime, t.EndTime, t.Notes, t.CreatedTimestamp })
            .ToListAsync(ct);

        var todoLists = await dbContext.TodoLists
            .AsNoTracking()
            .Where(tl => tl.UserId == userId)
            .Select(tl => new { tl.Id, tl.Name, tl.CreatedTimestamp })
            .ToListAsync(ct);

        var todoListItems = await dbContext.TodoListItems
            .AsNoTracking()
            .Where(i => i.UserId == userId)
            .Select(i => new { i.Id, i.Activity.Name, i.CreatedTimestamp })
            .ToListAsync(ct);

        var routineTodoLists = await dbContext.RoutineTodoLists
            .AsNoTracking()
            .Where(r => r.UserId == userId)
            .Select(r => new { r.Id, r.Activity.Name, r.CreatedTimestamp })
            .ToListAsync(ct);

        var templates = await dbContext.TaskPlannerDayTemplates
            .AsNoTracking()
            .Where(t => t.UserId == userId)
            .Select(t => new { t.Id, t.Name, t.Description, t.Icon, t.IsActive, t.CreatedTimestamp })
            .ToListAsync(ct);

        var calendars = await dbContext.Calendars
            .AsNoTracking()
            .Where(c => c.UserId == userId)
            .Select(c => new { c.Id, c.Date, c.CreatedTimestamp })
            .ToListAsync(ct);

        var activityHistories = await dbContext.ActivityHistories
            .AsNoTracking()
            .Where(a => a.UserId == userId)
            .Select(a => new { a.Id, a.StartTimestamp, a.Length, a.EndTimestamp, a.CreatedTimestamp })
            .ToListAsync(ct);

        var webTracking = await dbContext.WebExtensionActivityEntries
            .AsNoTracking()
            .Where(e => e.UserId == userId)
            .Select(e => new { e.Id, e.CreatedTimestamp })
            .ToListAsync(ct);

        var desktopTracking = await dbContext.DesktopActivityEntries
            .AsNoTracking()
            .Where(e => e.UserId == userId)
            .Select(e => new { e.Id, e.CreatedTimestamp })
            .ToListAsync(ct);

        // Both the stamp in the payload and the one in the filename are read on the account's own clocks, so
        // "the export I took on the 18th" names the same file the user would name. DateTimeOffset keeps the
        // instant unambiguous while still showing the offset the user lives in.
        var exportedAtUtc = DateTime.UtcNow;
        var exportedAt = new DateTimeOffset(exportedAtUtc, TimeSpan.Zero)
            .ToOffset(user.Timezone.GetUtcOffset(exportedAtUtc));

        var export = new
        {
            exportedAt,
            user = new
            {
                email = user.Email,
                createdAt = user.CreatedTimestamp,
                preferences = new
                {
                    theme = user.Theme.ToString().ToLowerInvariant(),
                    locale = user.Locale.ToString().ToUpperInvariant(),
                    timezone = user.Timezone.Id,
                    firstDayOfWeek = user.FirstDayOfWeek,
                    askBeforeDelete = user.AskBeforeDelete,
                    weatherLocation = user.WeatherLocation
                }
            },
            plannerTasks,
            todoLists,
            todoListItems,
            routineTodoLists,
            templates,
            calendars,
            activityTracking = new { activityHistories, webTracking, desktopTracking }
        };

        var json = JsonSerializer.Serialize(export, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        // WallClockZone.FromUtc, not DateTime.UtcNow: a user east of Greenwich taking an export late in the
        // evening would otherwise get a file dated tomorrow.
        var localDate = WallClockZone.FromUtc(exportedAtUtc, user.Timezone);
        var filename = $"antiprocrastination-export-{localDate:yyyy-MM-dd}.json";

        // Send.BytesAsync writes the attachment Content-Disposition itself -- same path the Reminders and
        // Scheduler exports take, rather than hand-rolling the header here.
        await Send.BytesAsync(Encoding.UTF8.GetBytes(json), filename, "application/json", cancellation: ct);
    }
}