using AdhdTimeOrganizer.application.dto.request.activity;
using AdhdTimeOrganizer.application.helper;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;

namespace AdhdTimeOrganizer.application.endpoint.activity.activity.command;

public class QuickEditActivityEndpoint(AppDbContext dbContext) : Endpoint<QuickEditActivityRequest>
{
    

    public override void Configure()
    {
        Put("/activity/{id:long:required}/quick-edit");
        Validator<QuickEditActivityValidator>();
        
        Summary(s =>
        {
            s.Summary = "Quick edit activity";
            s.Description = "Overwrites editable fields of an existing activity";
            s.Response(204, "Updated");
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
                AddError("Activity not found.");
                await Send.ErrorsAsync(404, ct);
                return;
            }

            entity.Name = req.Name;
            entity.Text = req.Text;
            entity.CategoryId = req.CategoryId;

            var result = await dbContext.UpdateEntityAsync(entity, ct);
            if (result.Failed)
            {
                AddError(result.ErrorMessage!);
                await Send.ErrorsAsync(400, ct);
                return;
            }

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
