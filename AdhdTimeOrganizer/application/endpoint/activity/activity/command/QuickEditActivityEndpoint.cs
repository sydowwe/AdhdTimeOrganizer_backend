using AdhdTimeOrganizer.application.dto.request.activity;
using AdhdTimeOrganizer.application.helper;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;

namespace AdhdTimeOrganizer.application.endpoint.activity.activity.command;

public class QuickEditActivityEndpoint(AppCommandDbContext dbContext) : Endpoint<QuickEditActivityRequest, long?>
{
    public virtual string[] AllowedRoles()
    {
        return EndpointHelper.GetUserOrHigherRoles();
    }

    public override void Configure()
    {
        Patch($"/activity/{{id}}/{{mode}}");
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

            var mode = Route<string>("mode");

            if (mode == "Overwrite")
            {
                entity.Name = req.Name;
                entity.Text = req.Text;
                entity.CategoryId = req.CategoryId;

                var result = await dbContext.UpdateEntityAsync(entity,ct);
                if (result.Failed)
                {
                    AddError(result.ErrorMessage!);
                    await SendErrorsAsync(400, ct);
                }
            }
            else if (mode == "Clone")
            {
                var clonedEntity = entity.Clone();

                clonedEntity.Name = req.Name;
                clonedEntity.Text = req.Text;
                clonedEntity.CategoryId = req.CategoryId;

                var result = await dbContext.AddEntityAsync(clonedEntity, ct);
                if (result.Failed)
                {
                    AddError(result.ErrorMessage!);
                    await SendErrorsAsync(400, ct);
                    return;
                }
                await SendOkAsync(clonedEntity.Id, ct);
            }
            else
            {
                AddError("Invalid mode.");
                await SendErrorsAsync(400, ct);
            }
        }
        catch (Exception ex)
        {
            var result = DbUtils.HandleException(ex, nameof(HandleAsync));
            AddError(result.ErrorMessage!);
            await SendErrorsAsync(400, ct);
        }
    }
}