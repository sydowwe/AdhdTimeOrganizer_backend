using System.Text;
using System.Text.Json;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

namespace AdhdTimeOrganizer.application.endpoint.user.read;

public class GetUserDataExportEndpoint(AppDbContext dbContext, IDistributedCache cache) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("user/data-export");
        Summary(s => { s.Summary = "Download all user data as a JSON file (max 1/min)"; });
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

        var export = new
        {
            exportedAt = DateTime.UtcNow,
            user = new
            {
                email = user.Email,
                createdAt = user.CreatedTimestamp,
                preferences = new
                {
                    theme = user.Theme.ToString().ToLowerInvariant(),
                    locale = user.CurrentLocale.ToString().ToUpperInvariant(),
                    timezone = user.Timezone.Id,
                    firstDayOfWeek = user.FirstDayOfWeek,
                    askBeforeDelete = user.AskBeforeDelete
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

        var filename = $"antiprocrastination-export-{DateTime.UtcNow:yyyy-MM-dd}.json";
        HttpContext.Response.Headers.ContentDisposition = $"attachment; filename=\"{filename}\"";
        HttpContext.Response.ContentType = "application/json";

        await HttpContext.Response.Body.WriteAsync(Encoding.UTF8.GetBytes(json), ct);
    }
}
