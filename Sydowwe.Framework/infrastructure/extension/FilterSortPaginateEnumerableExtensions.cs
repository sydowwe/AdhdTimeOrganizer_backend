using System.Collections.Concurrent;
using System.Reflection;
using Sydowwe.Framework.application.dto.request.generic;

namespace Sydowwe.Framework.infrastructure.extension;

public static class FilterSortPaginateEnumerableExtensions
{
    public static IEnumerable<T> SortBySingleAndPaginate<T>(this IEnumerable<T> collection, SortByRequest sortBy, int showPerPage, int currentPage)
        where T : class
    {
        var orderedCollection = collection.OrderByWithDirection(e => GetPropertyValue(e, sortBy.Key), sortBy.IsDesc);
        return orderedCollection.Skip((currentPage - 1) * showPerPage).Take(showPerPage);
    }

    public static IEnumerable<T> SortByManyAndPaginate<T>(this IEnumerable<T> collection, SortByRequest[] sortByList, int itemsPerPage, int page)
        where T : class
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(itemsPerPage, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        var items = collection.ToList();
        IOrderedEnumerable<T>? orderedCollection = null;

        //WARMING: Can fail with views without id
        if (sortByList.Length == 0)
        {
            orderedCollection = items.OrderBy(e => GetPropertyValue(e, "Id"));
        }
        else
        {
            var first = true;
            foreach (var sortBy in sortByList)
                if (first)
                {
                    orderedCollection = sortBy.IsDesc
                        ? items.OrderByDescending(e => GetPropertyValue(e, sortBy.Key))
                        : items.OrderBy(e => GetPropertyValue(e, sortBy.Key));
                    first = false;
                }
                else if (orderedCollection != null)
                {
                    orderedCollection = sortBy.IsDesc
                        ? orderedCollection.ThenByDescending(e => GetPropertyValue(e, sortBy.Key))
                        : orderedCollection.ThenBy(e => GetPropertyValue(e, sortBy.Key));
                }
        }

        return orderedCollection?.Skip((page - 1) * itemsPerPage).Take(itemsPerPage)
               ?? items.Skip((page - 1) * itemsPerPage).Take(itemsPerPage);
    }

    extension<TSource>(IEnumerable<TSource> source)
    {
        public IOrderedEnumerable<TSource> OrderByWithDirection<TKey>(Func<TSource, TKey> keySelector,
            bool descending = false) =>
            descending
                ? source.SmartOrderByDescending(keySelector)
                : source.SmartOrderBy(keySelector);

        private IOrderedEnumerable<TSource> SmartOrderBy<TKey>(Func<TSource, TKey> keySelector)
        {
            if (source is IOrderedEnumerable<TSource> orderedEnumerable)
                return orderedEnumerable.ThenBy(keySelector);

            return source.OrderBy(keySelector);
        }

        private IOrderedEnumerable<TSource> SmartOrderByDescending<TKey>(Func<TSource, TKey> keySelector)
        {
            if (source is IOrderedEnumerable<TSource> orderedEnumerable)
                return orderedEnumerable.ThenByDescending(keySelector);

            return source.OrderByDescending(keySelector);
        }
    }

    private static readonly ConcurrentDictionary<(Type, string), PropertyInfo?> PropertyCache = new();

    private static object? GetPropertyValue<T>(T entity, string propertyName)
    {
        var prop = PropertyCache.GetOrAdd(
            (typeof(T), propertyName.ToLowerInvariant()),
            key => key.Item1.GetProperty(key.Item2, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase));
        return prop?.GetValue(entity);
    }
}