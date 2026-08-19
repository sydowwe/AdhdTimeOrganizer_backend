using AdhdTimeOrganizer.Core.application.@event;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner;
using AdhdTimeOrganizer.Planning.application.validator;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.serviceContract;
using FastEndpoints;
using Humanizer;
using Sydowwe.Framework.domain.helper;
using Sydowwe.Framework.infrastructure.persistence;

namespace AdhdTimeOrganizer.Planning.application.endpoint.activityPlanning.plannerTask.command;

public class PatchPlannerTaskStatusEndpoint(DbContext dbContext, IReminderRegistrationService reminders) : Endpoint<PatchPlannerTaskStatusRequest>
{
    public override void Configure()
    {
        Patch($"/{nameof(PlannerTask).Kebaberize()}/{{id:long:required}}/status");

        Validator<PatchPlannerTaskStatusValidator>();
        Summary(s =>
        {
            s.Summary = "Update planner task status";
            s.Description = "Updates task status with side effects: resets ActualStart/EndTime for Cancelled/NotStarted statuses, publishes status change event if task date is today";
            s.Response(204, "Updated");
            s.Response(404, "Not found");
            s.Response(400, "Bad request or validation error");
            s.Response(409, "Concurrent write to the same task lost the row_version race");
            s.Response(500, "Internal server error");
        });
    }

    public override async Task HandleAsync(PatchPlannerTaskStatusRequest request, CancellationToken ct)
    {
        try
        {
            var entity = await dbContext.Set<PlannerTask>()
                .Include(pt => pt.Calendar)
                .FirstOrDefaultAsync(e => e.Id == Route<long>("id"), ct);

            if (entity is null)
            {
                AddError("PlannerTask not found.");
                await Send.ErrorsAsync(404, ct);
                return;
            }

            entity.ApplyStatus(request.Status);

            switch (request.Status)
            {
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

            // Finishing or calling off a task retires the reminder attached to it: a nudge about something
            // already dealt with is pure noise, and acting on the task is the user cancelling it implicitly.
            // Moving back to NotStarted/InProgress re-registers it — the same call covers both directions,
            // because the decision lives in the registration service, not here.
            await reminders.SyncForPlannerTasksAsync([entity.Id], ct);

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            if (entity.Calendar.Date == today)
                await new PlannerTaskIsDoneChangedEvent(entity.ActivityId, entity.UserId, entity.IsDone, entity.TodolistItemId)
                    .PublishAsync(Mode.WaitForAll, ct);

            await Send.NoContentAsync(ct);
        }
        catch (Exception ex)
        {
            // Same mapping every other write endpoint uses (see BasePatchEndpoint): a lost optimistic-
            // concurrency race on the row_version token is a 409, a unique/FK violation a 409, and only a
            // genuinely unrecognised fault stays a 500.
            var result = DbUtils.HandleException(ex, nameof(HandleAsync));
            AddError(result.ErrorMessage!);
            await Send.ErrorsAsync(EndpointHelper.ToStatusCode(result.ErrorType), ct);
        }
    }
}