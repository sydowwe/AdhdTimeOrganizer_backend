using AdhdTimeOrganizer.application.helper;
using AdhdTimeOrganizer.domain.model.entity;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Humanizer;

namespace AdhdTimeOrganizer.application.endpoint.todoList;

public abstract class BaseToggleStepIsDoneEndpoint<TEntity>(AppDbContext dbContext) : EndpointWithoutRequest
    where TEntity : BaseTodoListItem
{
    public override void Configure()
    {
        Patch(typeof(TEntity).Name.Kebaberize() + "/{itemId:long:required}/steps/{stepId:guid:required}/toggle");
        Roles(EndpointHelper.GetUserOrHigherRoles());
        Summary(s =>
        {
            var name = typeof(TEntity).Name;
            s.Summary = $"Toggles a step's IsDone on a {name}";
            s.Response(204, "Toggled");
            s.Response(400, "Bad request");
            s.Response(404, "Not found");
        });
    }

    protected long ItemId => Route<long>("itemId");
    protected Guid StepId => Route<Guid>("stepId");

    public override async Task HandleAsync(CancellationToken ct)
    {
        var item = await FetchItem(ItemId, ct);
        if (item is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var step = item.Steps.FirstOrDefault(s => s.Id == StepId);
        if (step is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var now = DateTime.UtcNow;
        var wasReset = BeforeToggle(item, now);
        ApplyStepToggle(item, step, now, wasReset);

        dbContext.Set<TEntity>().Update(item);
        await dbContext.SaveChangesAsync(ct);

        await PublishEvent(item, ct);
        await Send.NoContentAsync(ct);
    }

    protected abstract Task<TEntity?> FetchItem(long itemId, CancellationToken ct);
    /// <summary>Returns true if the item was reset (e.g. routine period expired), so the toggle starts fresh.</summary>
    protected virtual bool BeforeToggle(TEntity item, DateTime now) => false;
    protected abstract Task PublishEvent(TEntity item, CancellationToken ct);

    /// <summary>
    /// Toggles the step and updates the parent item accordingly.
    /// If the item was fully complete and a step is unchecked, it reopens the item.
    /// Pass wasReset=true when a period reset occurred before this toggle so the
    /// "all done?" check is not skewed by the other steps being wiped.
    /// </summary>
    protected void ApplyStepToggle(TEntity item, TodoListStep step, DateTime now, bool wasReset = false)
    {
        var wasFullyComplete = !wasReset && item.IsDone && (item.TotalCount is null || item.DoneCount >= item.TotalCount);
        step.IsDone = !step.IsDone;

        if (wasFullyComplete && !step.IsDone)
        {
            item.IsDone = false;
            if (item.TotalCount.HasValue && item.DoneCount > 0)
                item.DoneCount--;
            return;
        }

        var allDone = item.Steps.All(s => s.IsDone);

        if (allDone)
        {
            if (item.TotalCount.HasValue)
            {
                item.DoneCount = (item.DoneCount ?? 0) + 1;
                if (item.DoneCount >= item.TotalCount)
                    item.IsDone = true;
                foreach (var s in item.Steps)
                    s.IsDone = item.IsDone;
            }
            else
            {
                item.IsDone = true;
            }

            // if (item.IsDone)
            //     OnItemCompleted(item, now);
        }
        else
        {
            item.IsDone = false;
        }
    }

    protected virtual void OnItemCompleted(TEntity item, DateTime now) { }
}
