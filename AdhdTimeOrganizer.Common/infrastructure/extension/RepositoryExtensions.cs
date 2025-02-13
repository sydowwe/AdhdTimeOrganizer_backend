using System.Linq.Expressions;
using AdhdTimeOrganizer.Common.domain.model.entity;
using AdhdTimeOrganizer.Common.domain.model.entityInterface;
using AdhdTimeOrganizer.Common.domain.result;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.Common.infrastructure.extension;

public static class RepositoryExtensions
{
    /// <summary>
    ///     Retrieves a single entity or returns an error if none are found.
    ///     Returns an error in case of multiple results or other exceptions.
    /// </summary>
    public static async Task<RepositoryResult<T>> SingleOrErrorAsync<T>(
        this IQueryable<T> query,
        Expression<Func<T, bool>> predicate)
        where T : IEntity
    {
        try
        {
            var entity = await query.SingleOrDefaultAsync(predicate);
            if (entity == null)
                return RepositoryResult<T>.Error(
                    RepositoryErrorType.NotFound,
                    "Entity not found ");
            return RepositoryResult<T>.Successful(entity);
        }
        catch (System.Exception exception)
        {
            return exception switch
            {
                InvalidOperationException => RepositoryResult<T>.Error(
                    RepositoryErrorType.ExpectedSingleResult,
                    "Single result expected returned multiple results. Check query logic.",
                    exception.Message),
                _ => RepositoryResult<T>.Error(
                    RepositoryErrorType.UnknownError,
                    "An unknown exception occurred.",
                    exception.Message)
            };
        }
    }
}