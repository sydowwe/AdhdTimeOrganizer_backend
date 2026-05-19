using AdhdTimeOrganizer.application.dto.request.activity.profile;
using AdhdTimeOrganizer.application.helper;
using AdhdTimeOrganizer.application.mapper.activity.profile;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.domain.model.entity.activity.profile;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;

namespace AdhdTimeOrganizer.application.endpoint.activity.profile.project.command;

public class CreateActivityProjectProfileEndpoint(AppDbContext dbContext, ActivityProjectProfileMapper mapper)
    : Endpoint<ActivityProjectProfileRequest, long>
{
    public override void Configure()
    {
        Post("/activity-project-profile");
        Validator<CreateActivityProjectProfileValidator>();
        Summary(s =>
        {
            s.Summary = "Create ActivityProjectProfile";
            s.Response<long>(201, "Created");
            s.Response(400, "Bad request");
            s.Response(404, "Activity not found");
        });
    }

    public override async Task HandleAsync(ActivityProjectProfileRequest req, CancellationToken ct)
    {
        try
        {
            var entity = mapper.ToEntity(req);
            await dbContext.Set<ActivityProjectProfile>().AddAsync(entity, ct);
            await dbContext.SaveChangesAsync(ct);
            await Send.ResponseAsync(entity.Id, 201, ct);
        }
        catch (Exception ex)
        {
            var result = DbUtils.HandleException(ex, nameof(HandleAsync));
            AddError(result.ErrorMessage!);
            await Send.ErrorsAsync(400, ct);
        }
    }
}
