using System.Linq.Expressions;
using AdhdTimeOrganizer.Command.infrastructure.persistence;
using AdhdTimeOrganizer.Common.domain.model.entity;
using AdhdTimeOrganizer.Common.domain.repositoryContract;
using AdhdTimeOrganizer.Common.domain.result;
using AdhdTimeOrganizer.Common.infrastructure.repository;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AdhdTimeOrganizer.Command.infrastructure.repository.@base;

public class BaseCrudRepository<T, TContext>(TContext context) : BaseReadRepository<T, TContext>(context), IBaseCrudRepository<T>
    where T : BaseEntity
    where TContext: AppCommandDbContext
{
    public virtual async Task<RepositoryResult> AddAsync(T entity)
    {
        try
        {
            await dbSet.AddAsync(entity);
            await context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            return HandleException(ex, nameof(AddAsync));
        }

        return RepositoryResult.Successful();
    }

    public virtual async Task<RepositoryResult> AddRangeAsync(IEnumerable<T> entities)
    {
        try
        {
            foreach (var chunk in entities.Chunk(300))
            {
                await dbSet.AddRangeAsync(chunk);
                await context.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            return HandleException(ex, nameof(AddRangeAsync));
        }

        return RepositoryResult.Successful();
    }

    public virtual async Task<RepositoryResult> UpdateAsync(T entity)
    {
        try
        {
            dbSet.Update(entity);
            var affectedRows = await context.SaveChangesAsync();
            if (affectedRows == 0)
            {
                return RepositoryResult.Error(RepositoryErrorType.DatabaseError, "No rows were updated. Entity may not exist.");
            }
        }
        catch (Exception ex)
        {
            return HandleException(ex, nameof(UpdateAsync));
        }

        return RepositoryResult.Successful();
    }

    public async Task<RepositoryResult> DeleteAsync(long id)
    {
        try
        {
            var affectedRows = await dbSet.Where(ci => ci.Id == id).ExecuteDeleteAsync();
            return affectedRows > 0
                ? RepositoryResult.Successful()
                : RepositoryResult.Error(RepositoryErrorType.NotFound, $"Entity with ID {id} not found");
        }
        catch (Exception ex)
        {
            return HandleException(ex, nameof(DeleteAsync));
        }
    }
    public async Task<RepositoryResult> DeleteByAsync(Expression<Func<T, bool>> predicate)
    {
        try
        {
            var affectedRows = await dbSet.Where(predicate).ExecuteDeleteAsync();
            return affectedRows > 0
                ? RepositoryResult.Successful()
                : RepositoryResult.Error(RepositoryErrorType.NotFound, $"Entity with {predicate} not found");
        }
        catch (Exception ex)
        {
            return HandleException(ex, nameof(DeleteAsync));
        }
    }

    public async Task<RepositoryResult> DeleteAsync(T entity)
    {
        try
        {
            dbSet.Remove(entity);
            await context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            return HandleException(ex, nameof(DeleteAsync));
        }

        return RepositoryResult.Successful();
    }

    public virtual async Task<RepositoryResult> BatchDeleteAsync(Expression<Func<T, bool>> predicate)
    {
        try
        {
            var affectedRows = await dbSet.Where(predicate).ExecuteDeleteAsync();
            return affectedRows > 0
                ? RepositoryResult.Successful()
                : RepositoryResult.Error(RepositoryErrorType.NotFound, "No matching entities found for batch delete");
        }
        catch (Exception ex)
        {
            return HandleException(ex, nameof(BatchDeleteAsync));
        }
    }
    protected static RepositoryResult HandleException(Exception exception, string operation)
    {
        if (exception is DbUpdateException dbUpdateEx)
        {
            if (dbUpdateEx.InnerException is PostgresException pgEx)
                return pgEx.SqlState switch
                {
                    PostgresErrorCodes.UniqueViolation =>
                        RepositoryResult.Error(RepositoryErrorType.UniqueViolationError, $"Unique constraint violation during {operation}", pgEx.Message),
                    PostgresErrorCodes.ForeignKeyViolation =>
                        RepositoryResult.Error(RepositoryErrorType.ForeignKeyError, $"Foreign key constraint violation during {operation}", pgEx.Message),
                    PostgresErrorCodes.NotNullViolation =>
                        RepositoryResult.Error(RepositoryErrorType.NullConstraintError, $"Null constraint violation during {operation}", pgEx.Message),
                    _ =>
                        RepositoryResult.Error(RepositoryErrorType.DatabaseError, $"Unhandled PostgreSQL error during {operation}", pgEx.Message)
                };

            return RepositoryResult.Error(
                RepositoryErrorType.DatabaseError,
                $"Database update error during {operation}", dbUpdateEx.Message);
        }

        if (exception is DbUpdateConcurrencyException concurrencyEx)
            return RepositoryResult.Error(
                RepositoryErrorType.DbConcurrencyError,
                $"Concurrency conflict during {operation}", concurrencyEx.Message);

        return RepositoryResult.Error(
            RepositoryErrorType.UnknownError,
            $"An unexpected error occurred during {operation}", exception.Message);
    }
}