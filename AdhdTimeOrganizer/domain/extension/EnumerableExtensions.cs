using AdhdTimeOrganizer.domain.result;

namespace AdhdTimeOrganizer.domain.extension;

public static class EnumerableExtensions
{
    /// <summary>
    ///     Retrieves a single entity or returns an error if none are found.
    ///     Returns an error in case of multiple results or other exceptions.
    /// </summary>
    public static Result<T> SingleOrError<T>(
        this IEnumerable<T> source,
        Func<T, bool> predicate)
        where T : class
    {
        try
        {
            var entity = source.SingleOrDefault(predicate);
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
    public static Result<T> SingleOrError<T>(
        this IEnumerable<T> source)
        where T : class
    {
        try
        {
            var entity = source.SingleOrDefault();
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