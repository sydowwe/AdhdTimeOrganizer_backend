using System.Linq.Expressions;

namespace AdhdTimeOrganizer.infrastructure.extension;

public static class OrderQueryableExtensions
{
    extension<TSource>(IQueryable<TSource> source)
    {
        public IOrderedQueryable<TSource> OrderByWithDirection<TKey>
        (Expression<Func<TSource, TKey>> keySelector,
            bool descending = false)
        {
            return descending
                ? source.SmartOrderByDescending(keySelector)
                : source.SmartOrderBy(keySelector);
        }

        private bool IsOrdered()
        {
            ArgumentNullException.ThrowIfNull(source);

            return source.Expression.Type == typeof(IOrderedQueryable<TSource>);
        }

        private IOrderedQueryable<TSource> SmartOrderBy<TKey>(Expression<Func<TSource, TKey>> keySelector)
        {
            if (!source.IsOrdered())
                return source.OrderBy(keySelector);
            var orderedQuery = source as IOrderedQueryable<TSource>;
            return ((IOrderedQueryable<TSource>)source).ThenBy(keySelector);
        }

        private IOrderedQueryable<TSource> SmartOrderByDescending<TKey>(Expression<Func<TSource, TKey>> keySelector)
        {
            if (!source.IsOrdered())
                return source.OrderByDescending(keySelector);
            var orderedQuery = source as IOrderedQueryable<TSource>;
            return ((IOrderedQueryable<TSource>)source).ThenByDescending(keySelector);
        }
    }
}