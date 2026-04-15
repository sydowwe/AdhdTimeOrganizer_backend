using AdhdTimeOrganizer.application.dto.filter;
using AdhdTimeOrganizer.application.dto.response.toDoList;
using AdhdTimeOrganizer.application.endpoint.@base.read.pageFilterSort;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.application.mapper.todoList;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;
using Microsoft.EntityFrameworkCore;
using TodoListMapper = AdhdTimeOrganizer.application.mapper.todoList.TodoListMapper;

namespace AdhdTimeOrganizer.application.endpoint.todoList.todoList.query;

public class GetFilterSortTodoListEndpoint(AppDbContext dbContext, TodoListMapper mapper, TodoListCategoryMapper categoryMapper)
    : BaseFilterSortEndpoint<TodoList, TodoListResponse, TodoListFilterRequest, TodoListMapper>(dbContext, mapper)
{
    protected override IQueryable<TodoList> WithIncludes(IQueryable<TodoList> query)
        => query.Include(tl => tl.Category).Include(tl => tl.TodoListItemColl);

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

    protected override IQueryable<TodoListResponse> ProjectToResponse(IQueryable<TodoList> query)
    {
        return query.Select(e=>new TodoListResponse
        {
            Id = e.Id,
            Name = e.Name,
            Text = e.Text,
            Icon = e.Icon,
            Category = e.Category != null ? categoryMapper.ToResponse(e.Category) : null,
            ItemCount = e.TodoListItemColl.Count,
            CompletedCount = e.TodoListItemColl.Count(i => i.IsDone)
        });
    }
}
