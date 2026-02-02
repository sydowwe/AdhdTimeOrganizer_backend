using AdhdTimeOrganizer.application.dto.request.activityTracking;
using AdhdTimeOrganizer.application.dto.response.activityTracking;
using AdhdTimeOrganizer.infrastructure.security;
using FastEndpoints;

namespace AdhdTimeOrganizer.application.endpoint.activityTracking.command;

[AllowExtensionClients]
public class ActivityHeartbeatEndpoint : Endpoint<ActivityHeartbeatRequest, ActivityHeartbeatResponse>
{
    public override void Configure()
    {
        Post("activity-tracking/heartbeat");
        Policies("ActivityTracking");
        Summary(s =>
        {
            s.Summary = "Receive browser activity tracking heartbeat";
            s.Description = "Receives activity tracking data from the TimeOrganizer-ActivityTracking browser extension";
            s.Response<ActivityHeartbeatResponse>(200, "Success");
            s.Response(400, "Bad request");
            s.Response(401, "Unauthorized - Invalid or missing API key");
            s.Response(403, "Forbidden - Requires ActivityTracking role");
        });
    }

    public override async Task HandleAsync(ActivityHeartbeatRequest req, CancellationToken ct)
    {
        // TODO: Process events - store in database or forward to your existing system
        // For now, just acknowledge receipt

        var response = new ActivityHeartbeatResponse
        {
            Success = true,
            EventsProcessed = req.Events.Count,
            Message = $"Processed {req.Events.Count} events at {req.HeartbeatAt:O}"
        };

        await SendOkAsync(response, ct);
    }
}
