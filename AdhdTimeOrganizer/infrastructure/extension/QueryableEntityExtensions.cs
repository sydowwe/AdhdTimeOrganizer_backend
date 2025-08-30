using System.Linq.Expressions;
using AdhdTimeOrganizer.application.dto.request.generic;
using AdhdTimeOrganizer.application.dto.response.@base;
using AdhdTimeOrganizer.domain.model.entityInterface;
using AdhdTimeOrganizer.domain.model.@enum;
using AdhdTimeOrganizer.domain.result;
using Humanizer;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.infrastructure.extension;

public static class QueryableEntityExtensions
{
    public static async Task<Result<TResponse>> GetByIdMappedAsync<TEntity, TResponse>(this IQueryable<TEntity> query, long id, Expression<Func<TEntity, TResponse>> map)
        where TEntity : class, IBaseTableEntity
        where TResponse : class, IIdResponse
    {
        return await query.AsNoTracking().Select(map).SingleOrErrorAsync(e => e.Id == id);
    }

    public static async Task<Result<TEntity>> GetByIdAsync<TEntity>(this IQueryable<TEntity> query, long id, bool asNoTracking = true)
        where TEntity : class, IBaseTableEntity
    {
        return asNoTracking ? await query.AsNoTracking().SingleOrErrorAsync(e => e.Id == id) : await query.SingleOrErrorAsync(e => e.Id == id);
    }

    public static async Task<List<TResponse>> GetAllMappedAsync<TEntity, TResponse>(this IQueryable<TEntity> query, Expression<Func<TEntity, TResponse>> map)
        where TEntity : class, IEntity
        where TResponse : class, IMyResponse
    {
        return await query.AsNoTracking().Select(map).ToListAsync();
    }


    public static async Task<Result<int>> GetCountAsync<TEntity>(this IQueryable<TEntity> query)
        where TEntity : class, IEntity
    {
        int count;
        try
        {
            count = await query.CountAsync();
        }
        catch (Exception e)
        {
            return Result<int>.Error(ResultErrorType.UnknownError, "Argument null exception thrown", e.Message);
        }

        return Result<int>.Successful(count);
    }


    public static IQueryable<TEntity> SortBySingleAndPaginate<TEntity>(this IQueryable<TEntity> query, SortByRequest sortBy, int showPerPage, int currentPage)
        where TEntity : IEntity
    {
        Expression<Func<TEntity, object>> orderBy = p => EF.Property<TEntity>(p, sortBy.Key);
        query = query.OrderByWithDirection(orderBy, sortBy.IsDesc);

        return query.Skip((currentPage - 1) * showPerPage).Take(showPerPage);
    }

    public static IQueryable<TEntity> SortByManyAndPaginate<TEntity>(this IQueryable<TEntity> query, SortByRequest[] sortByList, int itemsPerPage, int page)
        where TEntity : IEntity
    {
        //WARMING: Can fail with views without id
        if (sortByList.Length == 0)
        {
            query = query.OrderBy(p => EF.Property<TEntity>(p, "Id"));
        }
        else
        {
            foreach (var sortBy in sortByList)
            {
                Expression<Func<TEntity, object>> orderBy = p => EF.Property<TEntity>(p, sortBy.Key.Pascalize());

                query = query.OrderByWithDirection(orderBy, sortBy.IsDesc);
            }
        }

        return query.Skip((page - 1) * itemsPerPage).Take(itemsPerPage);
    }


    public static async Task<BaseTableResponse<TResponse>> GetTableDataAsync<TEntity, TResponse>(
        this IQueryable<TEntity> query,
        SortByRequest[] sortBy,
        int itemsPerPage,
        int page,
        Func<TEntity, TResponse> mapping,
        CancellationToken cancellationToken = default
    )
        where TEntity : class, IEntity
        where TResponse : class, IIdResponse
    {
        // Get total count before pagination
        var itemsCount = await query.CountAsync(cancellationToken);
        var pageCount = (int)Math.Ceiling((double)itemsCount / itemsPerPage);

        // Apply sorting and pagination using your existing extension
        var paginatedQuery = query.SortByManyAndPaginate(sortBy, itemsPerPage, page);

        // Execute query and map results
        var entities = await paginatedQuery.ToListAsync(cancellationToken);
        var items = entities.Select(mapping).ToList();

        return new BaseTableResponse<TResponse>
        {
            Items = items,
            ItemsCount = itemsCount,
            PageCount = pageCount
        };
    }

    public static IQueryable<TEntity> AddEqualityOperatorFilter<TEntity, TProperty>(
        this IQueryable<TEntity> query,
        Expression<Func<TEntity, TProperty>> propertySelector,
        EqualityOperatorEnum operatorType,
        TProperty value)
        where TEntity : class
    {
        if (propertySelector == null)
            throw new ArgumentNullException(nameof(propertySelector));

        if (value == null)
            return query;

        var parameter = propertySelector.Parameters[0];
        var property = propertySelector.Body;
        var constant = Expression.Constant(value, typeof(TProperty));

        Expression comparison = operatorType switch
        {
            EqualityOperatorEnum.Equal => Expression.Equal(property, constant),
            EqualityOperatorEnum.NotEqual => Expression.NotEqual(property, constant),
            EqualityOperatorEnum.GreaterThan => Expression.GreaterThan(property, constant),
            EqualityOperatorEnum.LessThan => Expression.LessThan(property, constant),
            EqualityOperatorEnum.GreaterThanOrEqual => Expression.GreaterThanOrEqual(property, constant),
            EqualityOperatorEnum.LessThanOrEqual => Expression.LessThanOrEqual(property, constant),
            _ => throw new ArgumentException($"Unsupported operator: {operatorType}", nameof(operatorType))
        };

        var lambda = Expression.Lambda<Func<TEntity, bool>>(comparison, parameter);
        return query.Where(lambda);
    }

    public static async Task ReplaceCollectionAsync<TEntity, TRelatedEntity>(
        this DbContext dbContext,
        TEntity entity,
        Expression<Func<TEntity, IEnumerable<TRelatedEntity>>> navigationProperty,
        IEnumerable<long> newRelatedIds)
        where TEntity : class, IEntityWithId
        where TRelatedEntity : class, IEntityWithId
    {
        var collectionEntry = dbContext.Entry(entity).Collection(navigationProperty);
        await collectionEntry.LoadAsync();

        var currentCollection = collectionEntry.CurrentValue!.ToList();
        currentCollection.Clear();

        var newRelatedEntities = await dbContext.Set<TRelatedEntity>()
            .Where(x => newRelatedIds.Contains(x.Id))
            .ToListAsync();

        currentCollection.AddRange(newRelatedEntities);
        collectionEntry.CurrentValue = currentCollection;
    }

    public static async Task ReplaceCollectionAsync<TEntity, TRelatedEntity>(
        this DbContext dbContext,
        TEntity entity,
        Expression<Func<TEntity, IEnumerable<TRelatedEntity>>> navigationProperty,
        IEnumerable<TRelatedEntity> newRelatedEntities)
        where TEntity : class, IEntityWithId
        where TRelatedEntity : class, IEntityWithId
    {
        var collectionEntry = dbContext.Entry(entity).Collection(navigationProperty);
        await collectionEntry.LoadAsync();

        var currentCollection = collectionEntry.CurrentValue!.ToList();
        currentCollection.Clear();

        currentCollection.AddRange(newRelatedEntities);
        collectionEntry.CurrentValue = currentCollection;
    }
}