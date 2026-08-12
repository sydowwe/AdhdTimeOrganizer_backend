using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.TodoLists.application.endpoint.todoList.steps;

namespace AdhdTimeOrganizer.Routines.application.endpoint.todoList.routineTodoList.steps;

public class UpdateStepRoutineTodoListEndpoint(DbContext dbContext)
    : BaseUpdateStepEndpoint<RoutineTodoList>(dbContext)
{
    private readonly DbContext _dbContext = dbContext;

    protected override IQueryable<RoutineTodoList> GetParentQuery(long itemId, long userId)
    {
        return _dbContext.Set<RoutineTodoList>().Where(e => e.Id == itemId && e.UserId == userId);
    }
}