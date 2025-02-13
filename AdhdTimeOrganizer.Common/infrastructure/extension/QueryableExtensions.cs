using System.Linq.Expressions;

namespace AdhdTimeOrganizer.Common.infrastructure.extension;

public static class QueryableExtension
{
    public static bool IsOrdered<T>(this IQueryable<T> queryable) {
        ArgumentNullException.ThrowIfNull(queryable);

        return queryable.Expression.Type == typeof(IOrderedQueryable<T>);
    }

    public static IOrderedQueryable<T> SmartOrderBy<T, TKey>(this IQueryable<T> queryable, Expression<Func<T, TKey>> keySelector)
    {
        if (!queryable.IsOrdered())
            return queryable.OrderBy(keySelector);
        var orderedQuery = queryable as IOrderedQueryable<T>;
        return orderedQuery.ThenBy(keySelector);

    }

    public static IOrderedQueryable<T> SmartOrderByDescending<T, TKey>(this IQueryable<T> queryable, Expression<Func<T, TKey>> keySelector)
    {
        if (!queryable.IsOrdered())
            return queryable.OrderByDescending(keySelector);
        var orderedQuery = queryable as IOrderedQueryable<T>;
        return orderedQuery.ThenByDescending(keySelector);

    }

    public static IOrderedEnumerable<TSource> OrderByWithDirection<TSource,TKey>
    (this IEnumerable<TSource> source,
        Func<TSource, TKey> keySelector,
        bool descending)
    {
        return descending ? source.OrderByDescending(keySelector)
            : source.OrderBy(keySelector);
    }

    public static IOrderedQueryable<TSource> OrderByWithDirection<TSource,TKey>
    (this IQueryable<TSource> source,
        Expression<Func<TSource, TKey>> keySelector,
        bool descending = false)
    {
        return descending ? source.SmartOrderByDescending(keySelector)
            : source.SmartOrderBy(keySelector);
    }
}