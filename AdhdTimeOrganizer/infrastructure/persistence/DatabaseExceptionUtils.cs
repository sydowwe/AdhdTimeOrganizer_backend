using AdhdTimeOrganizer.domain.result;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AdhdTimeOrganizer.infrastructure.persistence;

public static class DbUtils
{
    public static Result HandleException(Exception exception, string operation)
    {
        if (exception is DbUpdateException dbUpdateEx)
        {
            if (dbUpdateEx.InnerException is PostgresException pgEx)
                return pgEx.SqlState switch
                {
                    PostgresErrorCodes.UniqueViolation =>
                        Result.Error(ResultErrorType.DbUniqueViolationError, $"Unique constraint violation during {operation}", pgEx.Message),
                    PostgresErrorCodes.ForeignKeyViolation =>
                        Result.Error(ResultErrorType.DbForeignKeyError, $"Foreign key constraint violation during {operation}", pgEx.Message),
                    PostgresErrorCodes.NotNullViolation =>
                        Result.Error(ResultErrorType.DbNullConstraintError, $"Null constraint violation during {operation}", pgEx.Message),
                    PostgresErrorCodes.CheckViolation =>
                        Result.Error(ResultErrorType.ValidationError, $"Validation error during {operation}", pgEx.Message),
                    PostgresErrorCodes.StringDataRightTruncation =>
                        Result.Error(ResultErrorType.ValidationError, $"Data too long during {operation}", pgEx.Message),

                    PostgresErrorCodes.NumericValueOutOfRange =>
                        Result.Error(ResultErrorType.ValidationError, $"Numeric value out of range during {operation}", pgEx.Message),

                    PostgresErrorCodes.DeadlockDetected =>
                        Result.Error(ResultErrorType.DbDeadlockError, $"Deadlock detected during {operation}", pgEx.Message),

                    PostgresErrorCodes.LockNotAvailable =>
                        Result.Error(ResultErrorType.DbLockNotAvailableError, $"Lock not available during {operation}", pgEx.Message),

                    PostgresErrorCodes.InsufficientPrivilege =>
                        Result.Error(ResultErrorType.DbPermissionError, $"Insufficient privileges during {operation}", pgEx.Message),

                    _ =>
                        Result.Error(ResultErrorType.DatabaseError, $"Unhandled PostgreSQL error during {operation}", pgEx.Message)
                };

            return Result.Error(
                ResultErrorType.DatabaseError,
                $"Database update error during {operation}", dbUpdateEx.Message);
        }

        if (exception is DbUpdateConcurrencyException concurrencyEx)
            return Result.Error(
                ResultErrorType.DbConcurrencyError,
                $"Concurrency conflict during {operation}", concurrencyEx.Message);

        return Result.Error(
            ResultErrorType.UnknownError,
            $"An unexpected error occurred during {operation}", exception.Message);
    }
}