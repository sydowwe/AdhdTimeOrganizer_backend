using AdhdTimeOrganizer.domain.model.entityInterface;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.todoList;

public class BaseToggleIsDoneTodoListEndpoint<TEntity>(AppCommandDbContext dbContext) : BaseToggleIsDoneEndpoint<TEntity>(dbContext)
    where TEntity : class, IEntityWithDoneAndTotalCount, IEntityWithIsDone
{
    protected override void IsDoneLogic(TEntity entity)
    {
        if (entity.TotalCount.HasValue)
        {
            if (entity.IsDone)
            {
                entity.DoneCount = 0;
                entity.IsDone = false;
                return;
            }

            entity.DoneCount ??= 0;
            entity.DoneCount++;
            if (entity.DoneCount == entity.TotalCount)
            {
                entity.IsDone = true;
            }
        }
        else
        {
            entity.IsDone = !entity.IsDone;
        }
    }
}