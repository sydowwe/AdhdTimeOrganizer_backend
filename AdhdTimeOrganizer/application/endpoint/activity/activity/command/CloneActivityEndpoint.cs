using AdhdTimeOrganizer.application.dto.request.activity;
using AdhdTimeOrganizer.application.helper;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;

namespace AdhdTimeOrganizer.application.endpoint.activity.activity.command;

public class CloneActivityEndpoint(AppDbContext dbContext) : Endpoint<QuickEditActivityRequest, long?>
{

    public override void Configure()
    {
        Post("/activity/{id:long:required}/clone");
        Validator<QuickEditActivityValidator>();
        Summary(s =>
        {
            s.Summary = "Clone activity";
            s.Description = "Creates a new activity cloned from an existing one";
            s.Response(200, "Cloned successfully");
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

            var clonedEntity = entity.Clone();
            clonedEntity.Name = clonedEntity.Name == req.Name ? $"{req.Name} (copy)" : req.Name;
            clonedEntity.Text = req.Text;
            clonedEntity.CategoryId = req.CategoryId;

            var result = await dbContext.AddEntityAsync(clonedEntity, ct);
            if (result.Failed)
            {
                AddError(result.ErrorMessage!);
                await Send.ErrorsAsync(400, ct);
                return;
            }

            await Send.OkAsync(clonedEntity.Id, ct);
        }
        catch (Exception ex)
        {
            var result = DbUtils.HandleException(ex, nameof(HandleAsync));
            AddError(result.ErrorMessage!);
            await Send.ErrorsAsync(400, ct);
        }
    }
}
