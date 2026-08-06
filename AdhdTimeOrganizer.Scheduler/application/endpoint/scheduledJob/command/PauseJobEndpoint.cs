using AdhdTimeOrganizer.Scheduler.application.dto.scheduledJob;
using FastEndpoints;
using MojaDigitalnaFirma.Kernel.scheduling;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.Scheduler.application.endpoint.scheduledJob.command;

/// <summary>Pause a job's trigger (stops firing until resumed). Idempotent. Open to any signed-in user (User/Admin/Root).</summary>
public class PauseJobEndpoint(IScheduler scheduler) : Endpoint<JobKeyRequest>
{
    public override void Configure()
    {
        Post("/scheduled-job/pause");
        Roles(this.GetUserRole());
        Summary(s =>
        {
            s.Summary = "Pause a recurring job by JobKey";
            s.Response(204, "Paused (idempotent)");
        });
    }

    public override async Task HandleAsync(JobKeyRequest req, CancellationToken ct)
    {
        await scheduler.PauseJobAsync(req.JobKey, ct);
        await Send.NoContentAsync(ct);
    }
}