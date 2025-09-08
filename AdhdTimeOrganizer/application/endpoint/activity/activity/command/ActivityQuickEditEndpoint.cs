using AdhdTimeOrganizer.application.dto.request.activity;
using AdhdTimeOrganizer.application.dto.response.activity;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.application.mapper.activity;
using AdhdTimeOrganizer.domain.helper;
using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Humanizer;

namespace AdhdTimeOrganizer.application.endpoint.activity.activity.command;

public class ActivityQuickEditEndpoint(AppCommandDbContext dbContext) : Endpoint<QuickEditActivityRequest>
{
    public virtual string[] AllowedRoles()
    {
        return EndpointHelper.GetUserOrHigherRoles();
    }

    public override void Configure()
    {
        Patch($"/activity/{{id}}");
        Roles(AllowedRoles());
        Summary(s =>
        {
            s.Summary = $"Quick edit activity";
            s.Description = $"Updates an existing activity";
            s.Response(204, "Success");
            s.Response(404, "Not found");
            s.Response(400, "Bad request");
        });
    }

    public override async Task HandleAsync(QuickEditActivityRequest req, CancellationToken ct)
    {
        try
        {
            var entity = await dbContext.Activities.FindAsync([Route<long>("id")], ct);
            if (entity == null)
            {
                await SendNotFoundAsync(ct);
                return;
            }

            entity.Name = req.Name;
            entity.Text = req.Text;
            entity.CategoryId = req.CategoryId;

            dbContext.Activities.Update(entity);
            var affectedRows = await dbContext.SaveChangesAsync(ct);
            if (affectedRows == 0)
            {
                AddError("No rows were updated. Entity may not exist.");
                await SendErrorsAsync(400, ct);
                return;
            }

            await SendNoContentAsync(ct);
        }
        catch (Exception ex)
        {
            var result = DbUtils.HandleException(ex, nameof(HandleAsync));
            AddError(result.ErrorMessage!);
            await SendErrorsAsync(400, ct);
        }
    }
}