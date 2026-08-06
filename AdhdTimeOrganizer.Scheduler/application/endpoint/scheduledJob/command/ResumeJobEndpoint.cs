using AdhdTimeOrganizer.Scheduler.application.dto.scheduledJob;
using FastEndpoints;
using MojaDigitalnaFirma.Kernel.scheduling;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.Scheduler.application.endpoint.scheduledJob.command;

/// <summary>Resume a paused job (recomputes NextRunAt). Idempotent. Open to any signed-in user (User/Admin/Root).</summary>
public class ResumeJobEndpoint(IScheduler scheduler) : Endpoint<JobKeyRequest>
{
    public override void Configure()
    {
        Post("/scheduled-job/resume");
        Roles(this.GetUserRole());
        Summary(s =>
        {
            s.Summary = "Resume a paused recurring job by JobKey";
            s.Response(204, "Resumed (idempotent)");
        });
    }

    public override async Task HandleAsync(JobKeyRequest req, CancellationToken ct)
    {
        await scheduler.ResumeJobAsync(req.JobKey, ct);
        await Send.NoContentAsync(ct);
    }
}