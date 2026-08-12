using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.infrastructure.persistence;

namespace AdhdTimeOrganizer.ActivityProfiles.application.endpoint.project.command;

public class DeleteActivityProjectProfileEndpoint(DbContext dbContext) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Delete("/activity-project-profile/{id:long:required}");
        Summary(s =>
        {
            s.Summary = "Delete ActivityProjectProfile";
            s.Response(204, "Success");
            s.Response(404, "Not found");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        try
        {
            var id = Route<long>("id");
            var userId = User.GetId();

            var entity = await dbContext.Set<ActivityProjectProfile>()
                .Include(p => p.Activity)
                .FirstOrDefaultAsync(p => p.Id == id, ct);

            if (entity is null || entity.Activity.UserId != userId)
            {
                AddError("ActivityProjectProfile not found.");
                await Send.ErrorsAsync(404, ct);
                return;
            }

            dbContext.Set<ActivityProjectProfile>().Remove(entity);
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