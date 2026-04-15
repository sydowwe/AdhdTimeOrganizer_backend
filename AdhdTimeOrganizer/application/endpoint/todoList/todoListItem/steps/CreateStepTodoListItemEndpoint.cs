using AdhdTimeOrganizer.application.endpoint.todoList.steps;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.todoList.todoListItem.steps;

public class CreateStepTodoListItemEndpoint(AppDbContext dbContext)
    : BaseCreateStepEndpoint<TodoListItem>(dbContext)
{
    protected override IQueryable<TodoListItem> GetParentQuery(long itemId, long userId) =>
        dbContext.Set<TodoListItem>().Where(e => e.Id == itemId && e.UserId == userId);
}
