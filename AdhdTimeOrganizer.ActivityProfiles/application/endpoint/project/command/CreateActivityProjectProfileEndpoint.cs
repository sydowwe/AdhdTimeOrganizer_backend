using AdhdTimeOrganizer.ActivityProfiles.application.dto.request;
using AdhdTimeOrganizer.ActivityProfiles.application.validator;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.infrastructure.persistence;

namespace AdhdTimeOrganizer.ActivityProfiles.application.endpoint.project.command;

public class CreateActivityProjectProfileEndpoint(DbContext dbContext)
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
            var entity = req.ToEntity;
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