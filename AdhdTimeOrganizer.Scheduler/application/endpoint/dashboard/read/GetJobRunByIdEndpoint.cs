using AdhdTimeOrganizer.Scheduler.application.dto.scheduledJobRun;
using AdhdTimeOrganizer.Scheduler.domain.entity;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.dto.request.generic;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.Scheduler.application.endpoint.dashboard.read;

/// <summary>
/// Single-run detail (GET <c>/scheduler-dashboard/run/{id}</c>): the run with its payload snapshot, error detail
/// and both directions of replay lineage. The reverse link (runs that replay this one) is a second query rather
/// than a base <c>GetById</c> projection, so it stays a pure read with no entity navigation added. Open to any signed-in user (User/Admin/Root).
/// </summary>
public class GetJobRunByIdEndpoint(DbContext dbContext) : Endpoint<IdRequest, ScheduledJobRunDetailDto>
{
    public override void Configure()
    {
        Get("/scheduler-dashboard/run/{id}");
        Roles(this.GetUserRole());
        Summary(s =>
        {
            s.Summary = "Dashboard: single run detail with payload + replay lineage";
            s.Response<ScheduledJobRunDetailDto>(200, "Success");
            s.Response(404, "Not found");
        });
    }

    public override async Task HandleAsync(IdRequest req, CancellationToken ct)
    {
        var runs = dbContext.Set<ScheduledJobRun>().AsNoTracking();

        var detail = await runs
            .Where(x => x.Id == req.Id)
            .Select(x => new ScheduledJobRunDetailDto
            {
                Id = x.Id,
                ScheduledJobId = x.ScheduledJobId,
                JobKeySnapshot = x.JobKeySnapshot,
                HandlerKeySnapshot = x.HandlerKeySnapshot,
                ScheduledFireTime = x.ScheduledFireTime,
                StartedAt = x.StartedAt,
                FinishedAt = x.FinishedAt,
                DurationMs = x.DurationMs,
                Outcome = x.Outcome,
                ErrorMessage = x.ErrorMessage,
                ErrorType = x.ErrorType,
                TriggerSource = x.TriggerSource,
                CorrelationId = x.CorrelationId,
                PayloadSnapshotJson = x.PayloadSnapshotJson,
                ReplaysRunId = x.ReplaysRunId,
                ReplayedByRunIds = new List<long>()
            })
            .FirstOrDefaultAsync(ct);

        if (detail is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        detail = detail with
        {
            ReplayedByRunIds = await runs
                .Where(x => x.ReplaysRunId == req.Id)
                .OrderBy(x => x.Id)
                .Select(x => x.Id)
                .ToListAsync(ct)
        };

        await Send.OkAsync(detail, ct);
    }
}