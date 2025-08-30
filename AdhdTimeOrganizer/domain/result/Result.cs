namespace AdhdTimeOrganizer.domain.result;

public interface IResult
{
    bool Failed { get; }
    ResultErrorType? ErrorType { get; }
    string? ErrorMessage { get; }
    string? ExceptionMessage { get; }
}

public class Result
{
    public bool Failed { get; protected init; }
    public ResultErrorType? ErrorType { get; protected init; }
    public string? ErrorMessage { get; protected init; }
    public string? ExceptionMessage { get; protected init; }


    public Result<TR> WithData<TR>(TR data)
        where TR : notnull
    {
        return Failed ? ToFailed<TR>() : Result<TR>.Successful(data);
    }

    public Result<T> ToFailed<T>()
    where T : notnull
    {
        return new Result<T>
        {
            Failed = true,
            ErrorType = ErrorType,
            ErrorMessage = ErrorMessage,
            ExceptionMessage = ExceptionMessage
        };
    }



    public static Result Successful()
    {
        return new Result
        {
            Failed = false
        };
    }
    public static Result<TR> Successful<TR>(TR data)
    where TR : notnull
    {
        return Result<TR>.Successful(data);
    }

    public static Result Error(ResultErrorType? errorType, string? errorMessage, string? exceptionMessage = null)
    {
        return new Result
        {
            Failed = true,
            ErrorType = errorType,
            ErrorMessage = errorMessage,
            ExceptionMessage = exceptionMessage
        };
    }
}

public interface IResult<out T> : IResult
    where T : notnull
{
    public T Data { get; }
    public Result<TR> To<TR>(Func<T, TR> mappingFunc) where TR : notnull;
}

public class Result<T> : Result, IResult<T>
    where T : notnull
{
    public T Data { get; private init; }

    public Result<TR> To<TR>(Func<T, TR> mappingFunc)
        where TR : notnull
    {
        return Failed ? ToFailed<TR>() : Result<TR>.Successful(mappingFunc.Invoke(Data));
    }
    public Result<T> ToFailed()
    {
        return new Result<T>
        {
            Failed = true,
            ErrorType = ErrorType,
            ErrorMessage = ErrorMessage,
            ExceptionMessage = ExceptionMessage
        };
    }



    public static Result<T> Successful(T data)
    {
        // if (typeof(T) != typeof(long) && !typeof(IEntity).IsAssignableFrom(typeof(T)))
        // {
        //     throw new ArgumentException("T must be IEntity or long.");
        // }
        return new Result<T>
        {
            Failed = false,
            Data = data
        };
    }

    public new static Result<T> Error(ResultErrorType? errorType, string? errorMessage, string? exceptionMessage = null)
    {
        return new Result<T>
        {
            Failed = true,
            ErrorType = errorType,
            ErrorMessage = errorMessage,
            ExceptionMessage = exceptionMessage
        };
    }
}