using AdhdTimeOrganizer.application.dto.filter;
using AdhdTimeOrganizer.application.dto.response.toDoList;
using AdhdTimeOrganizer.application.endpoint.@base.read.pageFilterSort;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;
using Microsoft.EntityFrameworkCore;
using TodoListMapper = AdhdTimeOrganizer.application.mapper.todoList.TodoListMapper;

namespace AdhdTimeOrganizer.application.endpoint.todoList.todoList.query;

public class GetFilterSortTodoListEndpoint(AppDbContext dbContext, TodoListMapper mapper)
    : BaseFilterSortEndpoint<TodoList, TodoListResponse, TodoListFilterRequest, TodoListMapper>(dbContext, mapper)
{
    protected override IQueryable<TodoList> WithIncludes(IQueryable<TodoList> query)
        => query.Include(tl => tl.Category);

    protected override IQueryable<TodoList> ApplyCustomFiltering(IQueryable<TodoList> query, TodoListFilterRequest filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.Name))
        {
            query = query.Where(tl => tl.Name.Contains(filter.Name));
        }

        if (filter.CategoryId.HasValue)
        {
            query = filter.CategoryId.Value == -1
                ? query.Where(tl => tl.CategoryId == null)
                : query.Where(tl => tl.CategoryId == filter.CategoryId.Value);
        }

        return query;
    }
}
