using AdhdTimeOrganizer.Scheduler.application.dto.scheduledJob;
using FastEndpoints;
using MojaDigitalnaFirma.Kernel.scheduling;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.Scheduler.application.endpoint.scheduledJob.command;

/// <summary>Unschedule + mark removed. Idempotent no-op on an unknown key. Open to any signed-in user (User/Admin/Root).</summary>
public class RemoveJobEndpoint(IScheduler scheduler) : Endpoint<JobKeyRequest>
{
    public override void Configure()
    {
        Post("/scheduled-job/remove");
        Roles(this.GetUserRole());
        Summary(s =>
        {
            s.Summary = "Remove (unschedule + mark removed) a recurring job by JobKey";
            s.Response(204, "Removed (idempotent)");
        });
    }

    public override async Task HandleAsync(JobKeyRequest req, CancellationToken ct)
    {
        await scheduler.RemoveJobAsync(req.JobKey, ct);
        await Send.NoContentAsync(ct);
    }
}