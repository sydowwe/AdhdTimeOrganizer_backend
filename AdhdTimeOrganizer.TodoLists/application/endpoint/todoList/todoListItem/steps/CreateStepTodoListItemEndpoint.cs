using AdhdTimeOrganizer.TodoLists.application.endpoint.todoList.steps;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;

namespace AdhdTimeOrganizer.TodoLists.application.endpoint.todoList.todoListItem.steps;

public class CreateStepTodoListItemEndpoint(DbContext dbContext)
    : BaseCreateStepEndpoint<TodoListItem>(dbContext)
{
    private readonly DbContext _dbContext = dbContext;

    protected override IQueryable<TodoListItem> GetParentQuery(long itemId, long userId)
    {
        return _dbContext.Set<TodoListItem>().Where(e => e.Id == itemId && e.UserId == userId);
    }
}