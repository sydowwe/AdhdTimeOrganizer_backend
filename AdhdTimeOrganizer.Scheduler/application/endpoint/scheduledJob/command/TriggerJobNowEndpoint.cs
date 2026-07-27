using AdhdTimeOrganizer.Scheduler.application.dto.scheduledJob;
using AdhdTimeOrganizer.Scheduler.domain.entity;
using FastEndpoints;
using MojaDigitalnaFirma.Kernel.scheduling;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.helper;
using Sydowwe.Framework.domain.serviceContract;

namespace AdhdTimeOrganizer.Scheduler.application.endpoint.scheduledJob.command;

/// <summary>
/// Fire a job once immediately, off-schedule (recorded as a Manual run). Works even when the job is paused.
/// Open to any signed-in user (User/Admin/Root).
/// </summary>
/// <remarks>
/// Trigger-now doesn't mutate the registry, so the CRUD audit interceptor sees nothing and the resulting
/// <c>ScheduledJobRun</c> is <c>[NoAudit]</c> with no <c>UserId</c> (the dispatcher runs unauthenticated). We
/// therefore emit a business-audit event from the endpoint â€” where the admin principal IS present â€” so the
/// privileged "fire arbitrary background work" act is attributable. <c>JobKey</c> is non-PII (safe to log).
/// </remarks>
public class TriggerJobNowEndpoint(IScheduler scheduler, IAuditService auditService) : Endpoint<JobKeyRequest>
{
    public override void Configure()
    {
        Post("/scheduled-job/trigger-now");
        Roles(IEndpoint.GetUserRole());
        Summary(s =>
        {
            s.Summary = "Fire a recurring job once immediately, off-schedule";
            s.Response(204, "Triggered");
        });
    }

    public override async Task HandleAsync(JobKeyRequest req, CancellationToken ct)
    {
        await scheduler.TriggerNowAsync(req.JobKey, ct);
        await auditService.LogAndSaveAsync("ScheduledJob.TriggeredNow",
            new { req.JobKey, TriggeredByUserId = User.GetId() }, nameof(ScheduledJob), ct);
        await Send.NoContentAsync(ct);
    }
}