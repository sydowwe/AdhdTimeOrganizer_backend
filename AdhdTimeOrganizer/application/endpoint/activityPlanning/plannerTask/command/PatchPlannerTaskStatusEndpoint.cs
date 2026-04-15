using AdhdTimeOrganizer.application.dto.request.taskPlanner;
using AdhdTimeOrganizer.application.@event;
using AdhdTimeOrganizer.application.helper;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.domain.model.@enum;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Humanizer;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.plannerTask.command;

public class PatchPlannerTaskStatusEndpoint(AppDbContext dbContext) : Endpoint<PatchPlannerTaskStatusRequest>
{
    public override void Configure()
    {
        Patch($"/{nameof(PlannerTask).Kebaberize()}/{{id}}/status");
        Roles(EndpointHelper.GetUserOrHigherRoles());
        Summary(s =>
        {
            s.Summary = "Patches PlannerTask status";
            s.Description = "Patches PlannerTask status";
            s.Response(204, "Updated");
            s.Response(404, "Not found");
        });
    }

    public override async Task HandleAsync(PatchPlannerTaskStatusRequest request, CancellationToken ct)
    {
        var entity = await dbContext.Set<PlannerTask>()
            .Include(pt => pt.Calendar)
            .FirstOrDefaultAsync(e => e.Id == Route<long>("id"), ct);

        if (entity is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        entity.Status = request.Status;

        switch (request.Status)
        {
            case PlannerTaskStatus.Cancelled:
            case PlannerTaskStatus.NotStarted:
                entity.ActualStartTime = null;
                entity.ActualEndTime = null;
                break;
            case PlannerTaskStatus.InProgress:
                if (request.ActualStartTime != null)
                    entity.ActualStartTime = request.ActualStartTime.ToTimeOnly();
                break;
            case PlannerTaskStatus.Completed:
                if (request.ActualStartTime != null)
                    entity.ActualStartTime = request.ActualStartTime.ToTimeOnly();
                if (request.ActualEndTime != null)
                    entity.ActualEndTime = request.ActualEndTime.ToTimeOnly();
                break;
        }

        dbContext.Set<PlannerTask>().Update(entity);
        await dbContext.SaveChangesAsync(ct);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (entity.Calendar.Date == today)
        {
            await new PlannerTaskIsDoneChangedEvent(entity.ActivityId, entity.UserId, entity.IsDone, entity.TodolistItemId)
                .PublishAsync(Mode.WaitForAll, ct);
        }

        await SendNoContentAsync(ct);
    }
}
