using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.TodoLists.application.endpoint.todoList.steps;

namespace AdhdTimeOrganizer.application.endpoint.todoList.routineTodoList.steps;

public class CreateStepRoutineTodoListEndpoint(AppDbContext dbContext)
    : BaseCreateStepEndpoint<RoutineTodoList>(dbContext)
{
    private readonly AppDbContext _dbContext = dbContext;

    protected override IQueryable<RoutineTodoList> GetParentQuery(long itemId, long userId)
    {
        return _dbContext.Set<RoutineTodoList>().Where(e => e.Id == itemId && e.UserId == userId);
    }
}