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

        // (long?) not (int?): DisplayOrder is a bigint, and the cast would both overflow once the
        // descending-by-gap scheme leaves int range and defeat the (user_id, task_priority_id,
        // display_order) index, since CAST(...) is not sargable.
        // HasValue, not a `!= 0` sentinel: MinAsync over long? already returns null for an empty
        // group, and 0 is a legal DisplayOrder — the group whose lowest item sits at exactly 0 would
        // otherwise be handed DisplayOrderStart again and collide with its own first item.
        var lastOrder = await query.MinAsync(e => (long?)e.DisplayOrder, ct);
        return lastOrder.HasValue ? lastOrder.Value - settings.DisplayOrderGap : settings.DisplayOrderStart;
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