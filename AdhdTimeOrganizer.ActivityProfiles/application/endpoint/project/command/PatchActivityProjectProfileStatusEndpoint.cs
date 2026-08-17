using AdhdTimeOrganizer.ActivityProfiles.application.dto.request;
using AdhdTimeOrganizer.ActivityProfiles.application.validator;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.infrastructure.persistence;

namespace AdhdTimeOrganizer.ActivityProfiles.application.endpoint.project.command;

public class PatchActivityProjectProfileStatusEndpoint(DbContext dbContext)
    : Endpoint<PatchActivityProjectProfileStatusRequest>
{
    public override void Configure()
    {
        Patch("/activity-project-profile/{id:long:required}/status");
        Validator<PatchActivityProjectProfileStatusValidator>();
        Summary(s =>
        {
            s.Summary = "Update ActivityProjectProfile readiness status";
            s.Description = "Status-only patch: sets ReadinessStatus and nothing else. No side effects.";
            s.Response(204, "Success");
            s.Response(404, "Not found");
            s.Response(400, "Bad request");
        });
    }

    public override async Task HandleAsync(PatchActivityProjectProfileStatusRequest req, CancellationToken ct)
    {
        try
        {
            var id = Route<long>("id");
            var userId = User.GetId();

            // ActivityProjectProfile is not IEntityWithUser and has no global query filter, so the
            // ownership check through Activity is the only thing keeping another user's row out.
            var entity = await dbContext.Set<ActivityProjectProfile>()
                .Include(p => p.Activity)
                .FirstOrDefaultAsync(p => p.Id == id, ct);

            if (entity is null || entity.Activity.UserId != userId)
            {
                AddError("ActivityProjectProfile not found.");
                await Send.ErrorsAsync(404, ct);
                return;
            }

            entity.ReadinessStatus = req.ReadinessStatus;
            await dbContext.SaveChangesAsync(ct);
            await Send.NoContentAsync(ct);
        }
        catch (Exception ex)
        {
            var result = DbUtils.HandleException(ex, nameof(HandleAsync));
            AddError(result.ErrorMessage!);
            await Send.ErrorsAsync(400, ct);
        }
    }
}
