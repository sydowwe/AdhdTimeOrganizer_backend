using AdhdTimeOrganizer.Planning.domain.model.@enum;
using AdhdTimeOrganizer.Planning.application.dto;
using AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner;
using AdhdTimeOrganizer.Planning.application.validator;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.serviceContract;
using FastEndpoints;
using Humanizer;
using Sydowwe.Framework.application.error;
using Sydowwe.Framework.domain.helper;
using Sydowwe.Framework.infrastructure.persistence;

namespace AdhdTimeOrganizer.Planning.application.endpoint.activityPlanning.plannerTask.command;

public class PatchPlannerTaskStatusEndpoint(
    DbContext dbContext,
    IPlannerTaskStatusEffects statusEffects) : Endpoint<PatchPlannerTaskStatusRequest>
{
    protected virtual string[] AllowedRoles() => this.GetDefaultRoles();

    public override void Configure()
    {
        Patch($"/{nameof(PlannerTask).Kebaberize()}/{{id:long:required}}/status");
        Roles(AllowedRoles());
        Validator<PatchPlannerTaskStatusValidator>();
        Summary(s =>
        {
            s.Summary = "Update planner task status";
            s.Description = "Updates task status with side effects: resets ActualStart/EndTime for Cancelled/NotStarted statuses, stores SkipReason or SkipReasonKey on Cancelled (a built-in label sent as text is stored as its key) and clears both on any other status, publishes status change event if task date is today";
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

            // The validator has already rejected an unknown key, so a failed parse here only means none was sent.
            SkipReasonKey? skipReasonKey = SkipReasonKeyTokens.TryParse(request.SkipReasonKey, out var key) ? key : null;
            entity.ApplyStatus(request.Status, request.SkipReason, skipReasonKey);

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

            // No Update() call: the entity came from the tracking query above, so SaveChanges already sees
            // the two or three properties that changed. Update() would mark all ~15 Modified and rewrite the
            // whole row — and once the audit interceptor is wired, record every column as changed on every
            // status patch.
            await dbContext.SaveChangesAsync(ct);

            // The reminder sync and the done-changed fan-out — shared with the push action buttons.
            await statusEffects.AfterStatusSavedAsync(entity, ct);

            await Send.NoContentAsync(ct);
        }
        catch (Exception ex)
        {
            // Same mapping every other write endpoint uses (see BasePatchEndpoint): a lost optimistic-
            // concurrency race on the row_version token is a 409, a unique/FK violation a 409, and only a
            // genuinely unrecognised fault stays a 500.
            var result = DbUtils.HandleException(ex, nameof(HandleAsync));
            AddError(result.ErrorMessage!);
            await Send.ErrorsAsync(this.ErrorStatus(result.ErrorType), ct);
        }
    }
}