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

public abstract class BaseCreateStepEndpoint<TParent>(DbContext dbContext)
    : Endpoint<CreateStepRequest, Guid>
    where TParent : BaseTodoListItem
{
    protected abstract IQueryable<TParent> GetParentQuery(long itemId, long userId);

    public override void Configure()
    {
        Post($"/{typeof(TParent).Name.Kebaberize()}/{{itemId}}/steps");
        Validator<CreateStepValidator>();

        Summary(s =>
        {
            s.Summary = $"Add a step to a {typeof(TParent).Name}";
            s.Response<Guid>(201, "Created — returns the new step ID");
            s.Response(404, "Not found");
        });
    }

    public override async Task HandleAsync(CreateStepRequest req, CancellationToken ct)
    {
        var parent = await GetParentQuery(req.ItemId, User.GetId())
            .Include(e => e.Steps)
            .FirstOrDefaultAsync(ct);

        if (parent is null)
        {
            AddError($"{typeof(TParent).Name} not found.");
            await Send.ErrorsAsync(404, ct);
            return;
        }

        var step = new TodoListStep { Name = req.Name, Order = req.Order, Note = req.Note };
        parent.Steps.Add(step);

        if (parent.DoneCount is null)
            parent.DoneCount = 0;

        var result = await dbContext.UpdateEntityAsync(parent, ct);
        if (result.Failed)
        {
            AddError(result.ErrorMessage ?? "An error occurred while adding the step.");
            await Send.ErrorsAsync(this.ErrorStatus(result.ErrorType), ct);
            return;
        }

        // Only after the save: the id is the caller's handle for the three step routes, so returning it for a
        // step that was never persisted hands out a 404 waiting to happen (CQ-10).
        await Send.ResponseAsync(step.Id, 201, ct);
    }
}