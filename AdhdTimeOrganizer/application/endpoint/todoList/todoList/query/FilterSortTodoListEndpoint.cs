using AdhdTimeOrganizer.application.dto.filter;
using AdhdTimeOrganizer.application.dto.response.todoList;
using AdhdTimeOrganizer.application.endpoint.@base.read.pageFilterSort;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.todoList.todoList.query;

public class FilterSortTodoListEndpoint(AppDbContext dbContext)
    : BaseFilterSortEndpoint<TodoList, TodoListResponse, TodoListFilterRequest>(dbContext)
{
    protected override IQueryable<TodoList> ApplyCustomFiltering(IQueryable<TodoList> query, TodoListFilterRequest filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.Name))
            query = query.Where(tl => tl.Name.Contains(filter.Name));

        if (filter.CategoryId.HasValue)
        {
            query = filter.CategoryId.Value == -1
                ? query.Where(tl => tl.CategoryId == null)
                : query.Where(tl => tl.CategoryId == filter.CategoryId.Value);
        }

        return query;
    }
}
