using System.Linq.Expressions;

namespace AdhdTimeOrganizer.infrastructure.extension;

public static class OrderQueryableExtensions
{


    public static IOrderedQueryable<TSource> OrderByWithDirection<TSource, TKey>
    (this IQueryable<TSource> source,
        Expression<Func<TSource, TKey>> keySelector,
        bool descending = false)
    {
        return descending
            ? source.SmartOrderByDescending(keySelector)
            : source.SmartOrderBy(keySelector);
    }

    private static bool IsOrdered<T>(this IQueryable<T> queryable)
    {
        ArgumentNullException.ThrowIfNull(queryable);

        return queryable.Expression.Type == typeof(IOrderedQueryable<T>);
    }

    private static IOrderedQueryable<T> SmartOrderBy<T, TKey>(this IQueryable<T> queryable, Expression<Func<T, TKey>> keySelector)
    {
        if (!queryable.IsOrdered())
            return queryable.OrderBy(keySelector);
        var orderedQuery = queryable as IOrderedQueryable<T>;
        return orderedQuery.ThenBy(keySelector);
    }

    private static IOrderedQueryable<T> SmartOrderByDescending<T, TKey>(this IQueryable<T> queryable, Expression<Func<T, TKey>> keySelector)
    {
        if (!queryable.IsOrdered())
            return queryable.OrderByDescending(keySelector);
        var orderedQuery = queryable as IOrderedQueryable<T>;
        return orderedQuery.ThenByDescending(keySelector);
    }
}