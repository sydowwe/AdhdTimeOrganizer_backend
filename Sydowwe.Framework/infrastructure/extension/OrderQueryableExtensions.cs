using System.Linq.Expressions;

namespace Sydowwe.Framework.infrastructure.extension;

public static class OrderQueryableExtensions
{
    public static IOrderedQueryable<TSource> OrderByWithDirection<TSource, TKey>
    (this IQueryable<TSource> source,
        Expression<Func<TSource, TKey>> keySelector,
        bool descending = false) =>
        descending
            ? source.SmartOrderByDescending(keySelector)
            : source.SmartOrderBy(keySelector);

    private static bool IsOrdered<T>(this IQueryable<T> queryable)
    {
        ArgumentNullException.ThrowIfNull(queryable);

        return queryable.Expression is MethodCallExpression { Method.Name: "OrderBy" or "OrderByDescending" or "ThenBy" or "ThenByDescending" };
    }

    private static IOrderedQueryable<T> SmartOrderBy<T, TKey>(this IQueryable<T> queryable, Expression<Func<T, TKey>> keySelector)
    {
        if (!queryable.IsOrdered())
            return queryable.OrderBy(keySelector);
        var orderedQuery = (IOrderedQueryable<T>)queryable;
        return orderedQuery.ThenBy(keySelector);
    }

    private static IOrderedQueryable<T> SmartOrderByDescending<T, TKey>(this IQueryable<T> queryable, Expression<Func<T, TKey>> keySelector)
    {
        if (!queryable.IsOrdered())
            return queryable.OrderByDescending(keySelector);
        var orderedQuery = (IOrderedQueryable<T>)queryable;
        return orderedQuery.ThenByDescending(keySelector);
    }
}