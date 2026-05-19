using AdhdTimeOrganizer.application.dto.request.activity.profile;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.application.helper;
using AdhdTimeOrganizer.application.mapper.activity.profile;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.domain.model.entity.activity.profile;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activity.profile.project.command;

public class UpdateActivityProjectProfileEndpoint(AppDbContext dbContext, ActivityProjectProfileMapper mapper)
    : Endpoint<ActivityProjectProfileRequest>
{
    public override void Configure()
    {
        Put("/activity-project-profile/{id:long:required}");
        Validator<UpdateActivityProjectProfileValidator>();
        Summary(s =>
        {
            s.Summary = "Update ActivityProjectProfile";
            s.Response(204, "Success");
            s.Response(404, "Not found");
            s.Response(400, "Bad request");
        });
    }

    public override async Task HandleAsync(ActivityProjectProfileRequest req, CancellationToken ct)
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

            mapper.UpdateEntity(req, entity);
            dbContext.Set<ActivityProjectProfile>().Update(entity);
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
