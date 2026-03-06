using AdhdTimeOrganizer.application.dto.request.activityTracking.desktop;
using AdhdTimeOrganizer.application.endpointGroups;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;

namespace AdhdTimeOrganizer.application.endpoint.activityTracking.desktop.command;

public class DesktopActivityHeartbeatEndpoint(AppDbContext dbContext) : Endpoint<DesktopActivityWindowDto>
{
    public override void Configure()
    {
        Post("/heartbeat");
        Validator<DesktopActivityHeartbeatValidator>();
        Group<ActivityTrackingDesktopGroup>();
    }

    public override async Task HandleAsync(DesktopActivityWindowDto req, CancellationToken ct)
    {
        var userId = User.GetId();
        var processedCount = 0;

        foreach (var entry in req.Entries.Where(e => e.ActiveSeconds != 0 || e.BackgroundSeconds != 0))
        {
            var record = new DesktopActivityEntry
            {
                UserId = userId,
                RecordDate = DateOnly.FromDateTime(req.WindowStart),
                WindowStart = req.WindowStart,
                ProcessName = entry.ProcessName,
                ProductName = entry.ProductName,
                WindowTitle = entry.WindowTitle,
                ExecutablePath = entry.ExecutablePath,
                IsFullscreen = entry.IsFullscreen,
                ActiveSeconds = entry.ActiveSeconds,
                BackgroundSeconds = entry.BackgroundSeconds,
                IsPlayingSound = entry.IsPlayingSound,
                ActiveMonitor = entry.ActiveMonitor,
            };

            dbContext.DesktopActivityEntries.Add(record);

            processedCount++;
        }

        await dbContext.SaveChangesAsync(ct);

        await SendAsync(processedCount, StatusCodes.Status201Created, ct);
    }
}