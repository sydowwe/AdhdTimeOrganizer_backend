using System.Linq.Expressions;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using AdhdTimeOrganizer.TodoLists.infrastructure.settings;

namespace AdhdTimeOrganizer.TodoLists.infrastructure.persistence.extensions;

public static class TodoListExtensions
{
    public static async Task<long> GetNextDisplayOrder<TEntity>(this DbSet<TEntity> dbSet, TodoListSettings settings, long userId, Expression<Func<TEntity, bool>>? groupFilter = null,
        CancellationToken ct = default)
        where TEntity : BaseTodoListItem
    {
        var query = dbSet.Where(e => e.UserId == userId);
        if (groupFilter != null)
            query = query.Where(groupFilter);

        var lastOrder = await query.MinAsync(e => (int?)e.DisplayOrder, ct) ?? 0;
        return lastOrder != 0 ? lastOrder - settings.DisplayOrderGap : settings.DisplayOrderStart;
    }

    public static async Task<long> GetNextDisplayOrder(this DbSet<TodoListItem> dbSet, TodoListSettings settings, long userId, long taskPriorityId, CancellationToken ct = default)
    {
        return await dbSet.GetNextDisplayOrder(settings, userId, e => e.TaskPriorityId == taskPriorityId, ct);
    }

    // userId is defense-in-depth alongside AppDbContext's global IEntityWithUser query filter, which
    // is what actually scopes this lookup under normal reads — explicit here so the filter isn't the
    // only thing standing between this method and a cross-user lookup under IgnoreQueryFilters().
    public static async Task<long?> GetDisplayOrderById<TEntity>(this DbSet<TEntity> dbSet, long id, long userId, CancellationToken ct = default) where TEntity : BaseTodoListItem
    {
        return await dbSet.Where(e => e.Id == id && e.UserId == userId)
            .Select(e => (long?)e.DisplayOrder)
            .FirstOrDefaultAsync(ct);
    }

    public static async Task<long?> GetGroupIdById<TEntity>(this DbSet<TEntity> dbSet, long id, long userId, Expression<Func<TEntity, long>> groupId, CancellationToken ct = default)
        where TEntity : BaseTodoListItem
    {
        return await dbSet
            .Where(e => e.Id == id && e.UserId == userId)
            .Select(groupId)
            .FirstOrDefaultAsync(ct);
    }
}