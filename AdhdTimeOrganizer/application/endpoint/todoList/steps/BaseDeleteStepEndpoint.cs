using AdhdTimeOrganizer.application.dto.request.toDoList;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.application.helper;
using AdhdTimeOrganizer.domain.model.entity;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Humanizer;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.todoList.steps;

public abstract class BaseDeleteStepEndpoint<TParent>(AppDbContext dbContext)
    : Endpoint<StepItemAndStepIdRequest>
    where TParent : BaseTodoListItem
{
    protected abstract IQueryable<TParent> GetParentQuery(long itemId, long userId);

    public override void Configure()
    {
        Delete($"{typeof(TParent).Name.Kebaberize()}/{{itemId}}/steps/{{stepId}}");
        Roles(EndpointHelper.GetUserOrHigherRoles());
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
            await SendNotFoundAsync(ct);
            return;
        }

        parent!.Steps.Remove(step);
        await dbContext.SaveChangesAsync(ct);
        await SendNoContentAsync(ct);
    }
}
