using AdhdTimeOrganizer.application.dto.request.toDoList;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.application.helper;
using AdhdTimeOrganizer.domain.model.entity;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Humanizer;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.todoList.steps;

public abstract class BaseCreateStepEndpoint<TParent>(AppDbContext dbContext)
    : Endpoint<CreateStepRequest, Guid>
    where TParent : BaseTodoListItem
{
    protected abstract IQueryable<TParent> GetParentQuery(long itemId, long userId);

    public override void Configure()
    {
        Post($"{typeof(TParent).Name.Kebaberize()}/{{itemId}}/steps");
        Roles(EndpointHelper.GetUserOrHigherRoles());
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
            await SendNotFoundAsync(ct);
            return;
        }

        var step = new TodoListStep { Name = req.Name, Order = req.Order, Note = req.Note };
        parent.Steps.Add(step);

        if (parent.DoneCount is null)
            parent.DoneCount = 0;

        await dbContext.SaveChangesAsync(ct);
        await SendAsync(step.Id, 201, ct);
    }
}
