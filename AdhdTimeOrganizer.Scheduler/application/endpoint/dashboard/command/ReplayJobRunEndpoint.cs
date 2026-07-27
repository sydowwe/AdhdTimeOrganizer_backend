using AdhdTimeOrganizer.Scheduler.domain.entity;
using AdhdTimeOrganizer.Scheduler.domain.serviceContract;
using FastEndpoints;
using Sydowwe.Framework.application.dto.request.generic;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.helper;
using Sydowwe.Framework.domain.result;
using Sydowwe.Framework.domain.serviceContract;

namespace AdhdTimeOrganizer.Scheduler.application.endpoint.dashboard.command;

/// <summary>
/// Replay a past run (POST <c>/scheduler-dashboard/run/{id}/replay</c>): re-run it <i>through</i> the phase-03
/// dispatcher with the original run's snapshotted payload, recorded as a new <c>Replay</c> run linked via
/// <c>ReplaysRunId</c> â€” the original row is never mutated. Thin wrapper over
/// <see cref="IScheduledRunReplayer"/>; builds no context and writes no run row itself. The dispatcher owns
/// the per-key concurrency gate, so a replay overlapping a live run of the same job is vetoed there.
/// <b>Replaying a non-idempotent job repeats its side effects</b> â€” the handler owns effect-idempotency.
/// Open to any signed-in user (User/Admin/Root).
/// <para>
/// Replay doesn't mutate the registry, so the CRUD audit interceptor sees nothing and the new
/// <c>ScheduledJobRun</c> is <c>[NoAudit]</c> with no <c>UserId</c>. Because replaying a non-idempotent job
/// repeats side effects, we emit a business-audit event here â€” where the admin principal IS present â€” so the
/// act is attributable. The original run id is non-PII (safe to log).
/// </para>
/// </summary>
public class ReplayJobRunEndpoint(IScheduledRunReplayer replayer, IAuditService auditService) : Endpoint<IdRequest>
{
    public override void Configure()
    {
        Post("/scheduler-dashboard/run/{id}/replay");
        Roles(IEndpoint.GetUserRole());
        Summary(s =>
        {
            s.Summary = "Dashboard: replay a past run through the dispatcher (new linked Replay run)";
            s.Response(204, "Replay fired");
            s.Response(404, "Run not found");
            s.Response(409, "Handler no longer registered, or job has been removed");
        });
    }

    public override async Task HandleAsync(IdRequest req, CancellationToken ct)
    {
        var result = await replayer.ReplayRunAsync(req.Id, ct);
        if (!result.Failed)
        {
            await auditService.LogAndSaveAsync("ScheduledJob.Replayed",
                new { RunId = req.Id, ReplayedByUserId = User.GetId() }, nameof(ScheduledJobRun), req.Id, ct);
            await Send.NoContentAsync(ct);
            return;
        }

        var status = result.ErrorType switch
        {
            ResultErrorType.NotFound => 404,
            ResultErrorType.Conflict => 409,
            _ => 400
        };
        AddError(result.ErrorMessage ?? "Replay failed.");
        await Send.ErrorsAsync(status, ct);
    }
}