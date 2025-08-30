using System.Reflection;
using AdhdTimeOrganizer.application.dto.request.generic;

namespace AdhdTimeOrganizer.infrastructure.extension;

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
        IOrderedEnumerable<T>? orderedCollection = null;

        //WARMING: Can fail with views without id
        if (sortByList.Length == 0)
        {
            orderedCollection = collection.OrderBy(e => GetPropertyValue(e, "Id"));
        }
        else
        {
            bool first = true;
            foreach (var sortBy in sortByList)
            {
                if (first)
                {
                    orderedCollection = sortBy.IsDesc
                        ? collection.OrderByDescending(e => GetPropertyValue(e, sortBy.Key))
                        : collection.OrderBy(e => GetPropertyValue(e, sortBy.Key));
                    first = false;
                }
                else if (orderedCollection != null)
                {
                    orderedCollection = sortBy.IsDesc
                        ? orderedCollection.ThenByDescending(e => GetPropertyValue(e, sortBy.Key))
                        : orderedCollection.ThenBy(e => GetPropertyValue(e, sortBy.Key));
                }
            }
        }

        return orderedCollection?.Skip((page - 1) * itemsPerPage).Take(itemsPerPage)
               ?? collection.Skip((page - 1) * itemsPerPage).Take(itemsPerPage);
    }

    public static IOrderedEnumerable<TSource> OrderByWithDirection<TSource, TKey>(
        this IEnumerable<TSource> source,
        Func<TSource, TKey> keySelector,
        bool descending = false)
    {
        return descending
            ? source.SmartOrderByDescending(keySelector)
            : source.SmartOrderBy(keySelector);
    }

    private static bool IsOrdered<T>(this IEnumerable<T> enumerable)
    {
        ArgumentNullException.ThrowIfNull(enumerable);
        return enumerable is IOrderedEnumerable<T>;
    }

    private static IOrderedEnumerable<T> SmartOrderBy<T, TKey>(this IEnumerable<T> enumerable, Func<T, TKey> keySelector)
    {
        if (!enumerable.IsOrdered())
            return enumerable.OrderBy(keySelector);

        var orderedEnumerable = enumerable as IOrderedEnumerable<T>;
        return orderedEnumerable!.ThenBy(keySelector);
    }

    private static IOrderedEnumerable<T> SmartOrderByDescending<T, TKey>(this IEnumerable<T> enumerable, Func<T, TKey> keySelector)
    {
        if (!enumerable.IsOrdered())
            return enumerable.OrderByDescending(keySelector);

        var orderedEnumerable = enumerable as IOrderedEnumerable<T>;
        return orderedEnumerable!.ThenByDescending(keySelector);
    }

    private static object? GetPropertyValue<T>(T entity, string propertyName)
    {
        return typeof(T).GetProperty(propertyName,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)?
            .GetValue(entity);
    }
}