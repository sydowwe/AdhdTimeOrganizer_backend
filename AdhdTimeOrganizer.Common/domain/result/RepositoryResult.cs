using AdhdTimeOrganizer.Common.domain.model.entity;
using AdhdTimeOrganizer.Common.domain.model.entityInterface;

namespace AdhdTimeOrganizer.Common.domain.result;

public class RepositoryResult
{
    public bool Failed { get; protected init; }
    public RepositoryErrorType? ErrorType { get; protected init; }
    public string? ErrorMessage { get; protected init; }
    public string? ExceptionMessage { get; protected init; }

    public static RepositoryResult Successful()
    {
        return new RepositoryResult
        {
            Failed = false
        };
    }

    public static RepositoryResult Error(RepositoryErrorType? errorType, string? errorMessage, string? exceptionMessage = null)
    {
        return new RepositoryResult
        {
            Failed = true,
            ErrorType = errorType,
            ErrorMessage = errorMessage,
            ExceptionMessage = exceptionMessage
        };
    }
}

public interface IRepositoryResult<out T>
    where T : notnull
{
    bool Failed { get; }
    RepositoryErrorType? ErrorType { get; }
    string? ErrorMessage { get; }
    string? ExceptionMessage { get; }
    public T Data { get; }
}

public class RepositoryResult<T> : RepositoryResult, IRepositoryResult<T>
    where T : notnull
{
    public T Data { get; private init; }

    public static RepositoryResult<T> Successful(T data)
    {
        if (typeof(T) != typeof(long) && !typeof(IEntity).IsAssignableFrom(typeof(T)))
        {
            throw new ArgumentException("T must be IEntity or long.");
        }
        return new RepositoryResult<T>
        {
            Failed = false,
            Data = data
        };
    }

    public new static RepositoryResult<T> Error(RepositoryErrorType? errorType, string? errorMessage, string? exceptionMessage = null)
    {
        return new RepositoryResult<T>
        {
            Failed = true,
            ErrorType = errorType,
            ErrorMessage = errorMessage,
            ExceptionMessage = exceptionMessage
        };
    }
}