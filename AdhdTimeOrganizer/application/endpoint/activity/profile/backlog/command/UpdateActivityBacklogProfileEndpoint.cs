using AdhdTimeOrganizer.application.dto.request.activity.profile;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.application.helper;
using AdhdTimeOrganizer.application.mapper.activity.profile;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.domain.model.entity.activity.profile;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activity.profile.backlog.command;

public class UpdateActivityBacklogProfileEndpoint(AppDbContext dbContext, ActivityBacklogProfileMapper mapper)
    : Endpoint<ActivityBacklogProfileRequest>
{
    public override void Configure()
    {
        Put("/activity-backlog-profile/{id:long:required}");
        Validator<UpdateActivityBacklogProfileValidator>();
        Summary(s =>
        {
            s.Summary = "Update ActivityBacklogProfile";
            s.Response(204, "Success");
            s.Response(404, "Not found");
            s.Response(400, "Bad request");
        });
    }

    public override async Task HandleAsync(ActivityBacklogProfileRequest req, CancellationToken ct)
    {
        try
        {
            var id = Route<long>("id");
            var userId = User.GetId();

            var entity = await dbContext.Set<ActivityBacklogProfile>()
                .Include(p => p.Activity)
                .FirstOrDefaultAsync(p => p.Id == id, ct);

            if (entity is null || entity.Activity.UserId != userId)
            {
                AddError("ActivityBacklogProfile not found.");
                await Send.ErrorsAsync(404, ct);
                return;
            }

            mapper.UpdateEntity(req, entity);
            dbContext.Set<ActivityBacklogProfile>().Update(entity);
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
