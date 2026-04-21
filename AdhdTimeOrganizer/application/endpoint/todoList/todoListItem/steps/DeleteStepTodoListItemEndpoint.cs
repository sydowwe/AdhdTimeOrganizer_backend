using AdhdTimeOrganizer.application.endpoint.todoList.steps;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.todoList.todoListItem.steps;

public class DeleteStepTodoListItemEndpoint(AppDbContext dbContext)
    : BaseDeleteStepEndpoint<TodoListItem>(dbContext)
{
    private readonly AppDbContext _dbContext = dbContext;

    protected override IQueryable<TodoListItem> GetParentQuery(long itemId, long userId) =>
        _dbContext.Set<TodoListItem>().Where(e => e.Id == itemId && e.UserId == userId);
}
