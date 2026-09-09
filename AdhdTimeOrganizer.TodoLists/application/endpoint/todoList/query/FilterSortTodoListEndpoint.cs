using AdhdTimeOrganizer.Core.infrastructure.persistence.extensions;
using AdhdTimeOrganizer.TodoLists.application.dto.filter;
using AdhdTimeOrganizer.TodoLists.application.dto.response.todoList;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using Sydowwe.Framework.application.endpoint.@base.read.pageFilterSort;

namespace AdhdTimeOrganizer.TodoLists.application.endpoint.todoList.todoList.query;

public class FilterSortTodoListEndpoint(DbContext dbContext)
    : BaseFilterSortEndpoint<TodoList, TodoListResponse, TodoListFilterRequest>(dbContext)
{
    protected override IQueryable<TodoList> ApplyCustomFiltering(IQueryable<TodoList> query, TodoListFilterRequest filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.Name))
        {
            var pattern = filter.Name.ToContainsPattern();
            query = query.Where(tl => EF.Functions.ILike(tl.Name, pattern, FreeTextFilterExtensions.EscapeCharacter));
        }

        if (filter.CategoryId.HasValue)
            query = filter.CategoryId.Value == TodoListCategoryResponse.UncategorizedId
                ? query.Where(tl => tl.CategoryId == null)
                : query.Where(tl => tl.CategoryId == filter.CategoryId.Value);

        return query;
    }
}