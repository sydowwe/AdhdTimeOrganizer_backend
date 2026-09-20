using AdhdTimeOrganizer.TodoLists.application.dto.request.todoList;
using AdhdTimeOrganizer.TodoLists.application.validator;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using FastEndpoints;
using Humanizer;
using Sydowwe.Framework.application.error;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.helper;
using Sydowwe.Framework.infrastructure.persistence;

namespace AdhdTimeOrganizer.TodoLists.application.endpoint.todoList.steps;

public abstract class BaseDeleteStepEndpoint<TParent>(DbContext dbContext)
    : Endpoint<StepItemAndStepIdRequest>
    where TParent : BaseTodoListItem
{
    protected abstract IQueryable<TParent> GetParentQuery(long itemId, long userId);

    public override void Configure()
    {
        Delete($"/{typeof(TParent).Name.Kebaberize()}/{{itemId}}/steps/{{stepId}}");
        Validator<StepItemAndStepIdValidator>();

        Summary(s =>
        {
            s.Summary = $"Delete a step from a {typeof(TParent).Name}";
            s.Response(204, "Deleted");
            s.Response(404, "Not found");
        });
    }

    public override async Task HandleAsync(StepItemAndStepIdRequest req, CancellationToken ct)
    {
        var parent = await GetParentQuery(req.ItemId, User.GetId())
            .Include(e => e.Steps)
            .FirstOrDefaultAsync(ct);

        var step = parent?.Steps.FirstOrDefault(s => s.Id == req.StepId);
        if (step is null)
        {
            AddError("Step not found.");
            await Send.ErrorsAsync(404, ct);
            return;
        }

        parent!.Steps.Remove(step);

        var result = await dbContext.UpdateEntityAsync(parent, ct);
        if (result.Failed)
        {
            AddError(result.ErrorMessage ?? "An error occurred while deleting the step.");
            await Send.ErrorsAsync(this.ErrorStatus(result.ErrorType), ct);
            return;
        }

        await Send.NoContentAsync(ct);
    }
}