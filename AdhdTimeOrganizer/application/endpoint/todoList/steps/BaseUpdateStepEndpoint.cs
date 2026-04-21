using AdhdTimeOrganizer.application.dto.request.toDoList;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.application.helper;
using AdhdTimeOrganizer.domain.model.entity;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Humanizer;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.todoList.steps;

public abstract class BaseUpdateStepEndpoint<TParent>(AppDbContext dbContext)
    : Endpoint<UpdateStepRequest>
    where TParent : BaseTodoListItem
{
    protected abstract IQueryable<TParent> GetParentQuery(long itemId, long userId);

    public override void Configure()
    {
        Put($"{typeof(TParent).Name.Kebaberize()}/{{itemId}}/steps/{{stepId}}");
        Roles(EndpointHelper.GetUserOrHigherRoles());
        Summary(s =>
        {
            s.Summary = $"Update a step on a {typeof(TParent).Name}";
            s.Response(204, "Updated");
            s.Response(404, "Not found");
        });
    }

    public override async Task HandleAsync(UpdateStepRequest req, CancellationToken ct)
    {
        var parent = await GetParentQuery(req.ItemId, User.GetId())
            .Include(e => e.Steps)
            .FirstOrDefaultAsync(ct);

        var step = parent?.Steps.FirstOrDefault(s => s.Id == req.StepId);
        if (step is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        step.Name = req.Name;
        step.Order = req.Order;
        step.Note = req.Note;

        await dbContext.SaveChangesAsync(ct);
        await Send.NoContentAsync(ct);
    }
}
