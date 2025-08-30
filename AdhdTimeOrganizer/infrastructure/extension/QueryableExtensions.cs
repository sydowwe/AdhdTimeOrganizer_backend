using System.Linq.Expressions;
using AdhdTimeOrganizer.domain.result;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.infrastructure.extension;

public static class QueryableExtension
{
    /// <summary>
    ///     Retrieves a single entity or returns an error if none are found.
    ///     Returns an error in case of multiple results or other exceptions.
    /// </summary>
    public static async Task<Result<T>> SingleOrErrorAsync<T>(this IQueryable<T> query,
        Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
        where T : class
    {
        try
        {
            var entity = await query.SingleOrDefaultAsync(predicate);
            if (entity == null)
                return Result<T>.Error(
                    ResultErrorType.NotFound,
                    "Entity not found ");
            return Result<T>.Successful(entity);
        }
        catch (System.Exception exception)
        {
            return exception switch
            {
                InvalidOperationException => Result<T>.Error(
                    ResultErrorType.ExpectedSingleResult,
                    "Single result expected returned multiple results. Check query logic.",
                    exception.Message),
                _ => Result<T>.Error(
                    ResultErrorType.UnknownError,
                    "An unknown exception occurred.",
                    exception.Message)
            };
        }
    }

    /// <summary>
    ///     Retrieves a single entity or returns an error if none are found.
    ///     Returns an error in case of multiple results or other exceptions.
    /// </summary>
    public static async Task<Result<T>> SingleOrErrorAsync<T>(
        this IQueryable<T> query)
        where T : class
    {
        try
        {
            var entity = await query.SingleOrDefaultAsync();
            if (entity == null)
                return Result<T>.Error(
                    ResultErrorType.NotFound,
                    "Entity not found ");
            return Result<T>.Successful(entity);
        }
        catch (System.Exception exception)
        {
            return exception switch
            {
                InvalidOperationException => Result<T>.Error(
                    ResultErrorType.ExpectedSingleResult,
                    "Single result expected returned multiple results. Check query logic.",
                    exception.Message),
                _ => Result<T>.Error(
                    ResultErrorType.UnknownError,
                    "An unknown exception occurred.",
                    exception.Message)
            };
        }
    }
}