using AdhdTimeOrganizer.Core.application.dto.request.activity.profile;
using AdhdTimeOrganizer.Core.application.validator;
using AdhdTimeOrganizer.Core.domain.model.entity.activity.profile;
using FastEndpoints;
using Sydowwe.Framework.infrastructure.persistence;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.Core.application.endpoint.activity.profile.backlog.command;

public class CreateActivityBacklogProfileEndpoint(DbContext dbContext)
    : Endpoint<ActivityBacklogProfileRequest, long>
{
    public override void Configure()
    {
        Post("/activity-backlog-profile");
        Validator<CreateActivityBacklogProfileValidator>();
        Summary(s =>
        {
            s.Summary = "Create ActivityBacklogProfile";
            s.Response<long>(201, "Created");
            s.Response(400, "Bad request");
            s.Response(404, "Activity not found");
        });
    }

    public override async Task HandleAsync(ActivityBacklogProfileRequest req, CancellationToken ct)
    {
        try
        {
            var entity = req.ToEntity;
            await dbContext.Set<ActivityBacklogProfile>().AddAsync(entity, ct);
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