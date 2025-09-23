using System.Linq.Expressions;
using AdhdTimeOrganizer.domain.model.entity;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Options;

namespace AdhdTimeOrganizer.infrastructure.persistence.extensions;

public static class TodoListExtensions
{
    public static async Task<long> GetNextDisplayOrder<TEntity>(this DbSet<TEntity> dbSet, TodoListSettings settings, long userId, Expression<Func<TEntity, bool>>? groupFilter = null, CancellationToken ct = default)
        where TEntity : BaseTodoList
    {
        var query = dbSet.Where(e => e.UserId == userId);
        if (groupFilter != null)
        {
            query = query.Where(groupFilter);
        }

        var lastOrder = await query.MinAsync(e => (int?)e.DisplayOrder, ct) ?? 0;
        return lastOrder != 0 ? lastOrder - settings.DisplayOrderGap : settings.DisplayOrderStart;
    }

    public static async Task<long> GetNextDisplayOrder(this DbSet<RoutineTodoList> dbSet, TodoListSettings settings, long userId, long timePeriodId, CancellationToken ct = default)
    {
        return await dbSet.GetNextDisplayOrder(settings, userId, e => e.TimePeriodId == timePeriodId, ct);
    }

    public static async Task<long> GetNextDisplayOrder(this DbSet<TodoList> dbSet, TodoListSettings settings, long userId, long timePeriodId, CancellationToken ct = default)
    {
        return await dbSet.GetNextDisplayOrder(settings, userId, e => e.TaskUrgencyId == timePeriodId, ct);
    }


    public static async Task<long?> GetDisplayOrderById<TEntity>(this DbSet<TEntity> dbSet, long id, CancellationToken ct = default) where TEntity : BaseTodoList
    {
        return await dbSet.Where(e => e.Id == id)
            .Select(e => (long?)e.DisplayOrder)
            .FirstOrDefaultAsync(ct);
    }

    public static async Task<long?> GetGroupIdById<TEntity>(this DbSet<TEntity> dbSet, long id, Expression<Func<TEntity, long>> groupId, CancellationToken ct = default) where TEntity : BaseTodoList
    {
        return await dbSet
            .Where(e => e.Id == id)
            .Select(groupId)
            .FirstOrDefaultAsync(ct);
    }

}