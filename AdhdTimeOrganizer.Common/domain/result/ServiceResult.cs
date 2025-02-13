namespace AdhdTimeOrganizer.Common.domain.result;

public class ServiceResult
{
    public bool Failed { get; protected init; }
    public ServiceErrorType? ErrorType { get; protected init; }
    public string? ErrorMessage { get; protected init; }

    public static ServiceResult Successful()
    {
        return new ServiceResult
        {
            Failed = false
        };
    }

    public static ServiceResult Error(ServiceErrorType? errorType, string? errorMessage)
    {
        return new ServiceResult
        {
            Failed = true,
            ErrorType = errorType,
            ErrorMessage = errorMessage
        };
    }
}

public interface IServiceResult<out T> where T : notnull
{
    T? Data { get; }
    bool Failed { get; }
    ServiceErrorType? ErrorType { get; }
    string? ErrorMessage { get; }
}

public class ServiceResult<T> : ServiceResult, IServiceResult<T> where T : notnull
{
    public T Data { get; private init; }

    public static ServiceResult<T> Successful(T? data)
    {
        if (data == null)
            throw new ArgumentNullException(nameof(data), "Data cannot be null on success.");
        return new ServiceResult<T>
        {
            Failed = false,
            Data = data
        };
    }

    public new static ServiceResult<T> Error(ServiceErrorType? errorType, string? errorMessage)
    {
        return new ServiceResult<T>
        {
            Failed = true,
            ErrorType = errorType,
            ErrorMessage = errorMessage
        };
    }

    public static ServiceResult<T> Error(ServiceResult serviceResult)
    {
        return new ServiceResult<T>
        {
            Failed = true,
            ErrorType = serviceResult.ErrorType,
            ErrorMessage = serviceResult.ErrorMessage
        };
    }
}