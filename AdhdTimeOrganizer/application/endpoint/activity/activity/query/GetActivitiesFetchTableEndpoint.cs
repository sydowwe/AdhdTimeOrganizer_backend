using AdhdTimeOrganizer.application.dto.filter;
using AdhdTimeOrganizer.application.dto.response.activity;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper.activity;
using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.infrastructure.persistence;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activity.activity.query;

public class GetActivitiesFetchTableEndpoint(
    AppCommandDbContext dbContext,
    ActivityMapper mapper) 
    : BaseFetchTableEndpoint<Activity, ActivityResponse, ActivityFilterRequest, ActivityMapper>(dbContext, mapper)
{
    protected override IQueryable<Activity> WithIncludes(IQueryable<Activity> query)
    {
        return query
            .Include(a => a.Role)
            .Include(a => a.Category);
    }

    protected override IQueryable<Activity> ApplyCustomFiltering(IQueryable<Activity> query, ActivityFilterRequest filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.Name))
        {
            query = query.Where(a => a.Name.Contains(filter.Name));
        }

        if (!string.IsNullOrWhiteSpace(filter.Text))
        {
            query = query.Where(a => a.Text != null && a.Text.Contains(filter.Text));
        }

        if (filter.IsUnavoidable.HasValue)
        {
            query = query.Where(a => a.IsUnavoidable == filter.IsUnavoidable.Value);
        }

        if (filter.IsOnTodoList.HasValue)
        {
            query = query.Include(a => a.TodoList);

           query = query.Where(a=> a.TodoList != null == filter.IsOnTodoList.Value );
        }

        if (filter.IsOnRoutineTodoList.HasValue)
        {
            query = query.Include(a => a.RoutineTodoLists);

            query = query.Where(a => a.RoutineTodoLists.Any() == filter.IsOnRoutineTodoList.Value);
        }

        if (filter.RoleId.HasValue)
        {
            query = query.Where(a => a.RoleId == filter.RoleId.Value);
        }

        if (filter.CategoryId.HasValue)
        {
            query = query.Where(a => a.CategoryId == filter.CategoryId.Value);
        }

        if (filter.UserId.HasValue)
        {
            query = query.Where(a => a.UserId == filter.UserId.Value);
        }

        return query;
    }
}
