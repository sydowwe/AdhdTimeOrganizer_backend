using AdhdTimeOrganizer.application.endpoint.todoList.steps;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.todoList.routineTodoList.steps;

public class DeleteStepRoutineTodoListEndpoint(AppDbContext dbContext)
    : BaseDeleteStepEndpoint<RoutineTodoList>(dbContext)
{
    protected override IQueryable<RoutineTodoList> GetParentQuery(long itemId, long userId) =>
        dbContext.Set<RoutineTodoList>().Where(e => e.Id == itemId && e.UserId == userId);
}
