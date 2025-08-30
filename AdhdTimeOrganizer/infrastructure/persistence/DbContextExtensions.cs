using AdhdTimeOrganizer.domain.model.entity;
using AdhdTimeOrganizer.domain.model.entityInterface;
using AdhdTimeOrganizer.domain.result;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.infrastructure.persistence;

public static class DbContextHelper
{
    public static void BaseSaveChangesAsync(this DbContext dbContext)
    {
        foreach (var entry in dbContext.ChangeTracker.Entries<BaseTableEntity>())
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedTimestamp = DateTime.UtcNow;
                    break;
                case EntityState.Modified:
                    entry.Entity.ModifiedTimestamp = DateTime.UtcNow;
                    break;
            }
    }

    public static async Task<Result> AddEntityAsync<TEntity>(this DbContext dbContext, TEntity entity, CancellationToken ct = default) where TEntity : class
    {
        try
        {
            await dbContext.Set<TEntity>().AddAsync(entity, ct);
            dbContext.BaseSaveChangesAsync();
            await dbContext.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            return DbUtils.HandleException(ex, nameof(AddEntityAsync));
        }

        return Result.Successful();
    }

    public static async Task<Result> AddRangeAsync<TEntity>(this DbContext dbContext, IEnumerable<TEntity> entities, CancellationToken ct = default) where TEntity : class
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);
        try
        {
            foreach (var chunk in entities.Chunk(300))
            {
                await dbContext.Set<TEntity>().AddRangeAsync(chunk, ct);
                dbContext.BaseSaveChangesAsync();
                await dbContext.SaveChangesAsync(ct);
            }

            await transaction.CommitAsync(ct);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(ct);
            return DbUtils.HandleException(ex, nameof(AddRangeAsync));
        }

        return Result.Successful();
    }

    public static async Task<Result> UpdateEntityAsync<TEntity>(this DbContext dbContext, TEntity entity, CancellationToken ct = default) where TEntity : class
    {
        try
        {
            dbContext.Set<TEntity>().Update(entity);
            dbContext.BaseSaveChangesAsync();
            var affectedRows = await dbContext.SaveChangesAsync(ct);
            if (affectedRows == 0)
            {
                return Result.Error(ResultErrorType.DatabaseError, "No rows were updated. Entity may not exist.");
            }
        }
        catch (Exception ex)
        {
            return DbUtils.HandleException(ex, nameof(UpdateEntityAsync));
        }

        return Result.Successful();
    }

    public static async Task<Result> UpdateRangeAsync<TEntity>(this DbContext dbContext, IEnumerable<TEntity> entities, CancellationToken ct = default) where TEntity : class
    {
        try
        {
            dbContext.Set<TEntity>().UpdateRange(entities);
            dbContext.BaseSaveChangesAsync();
            var affectedRows = await dbContext.SaveChangesAsync(ct);
            if (affectedRows == 0)
            {
                return Result.Error(ResultErrorType.DatabaseError, "No rows were updated. Entities may not exist.");
            }
        }
        catch (Exception ex)
        {
            return DbUtils.HandleException(ex, nameof(UpdateRangeAsync));
        }

        return Result.Successful();
    }

    public static async Task<Result> DeleteEntityAsync<TEntity>(this DbContext dbContext, TEntity entity, CancellationToken ct = default) where TEntity : class
    {
        try
        {
            dbContext.Set<TEntity>().Remove(entity);
            var affectedRows = await dbContext.SaveChangesAsync(ct);
            if (affectedRows == 0)
            {
                return Result.Error(ResultErrorType.DatabaseError, "No rows were deleted. Entity may not exist.");
            }
        }
        catch (Exception ex)
        {
            return DbUtils.HandleException(ex, nameof(DeleteEntityAsync));
        }

        return Result.Successful();
    }

    public static async Task<Result> DeleteRangeAsync<TEntity>(this DbContext dbContext, IEnumerable<TEntity> entities, CancellationToken ct = default) where TEntity : class
    {
        try
        {
            dbContext.Set<TEntity>().RemoveRange(entities);
            var affectedRows = await dbContext.SaveChangesAsync(ct);
            if (affectedRows == 0)
            {
                return Result.Error(ResultErrorType.DatabaseError, "No rows were deleted. Entities may not exist.");
            }
        }
        catch (Exception ex)
        {
            return DbUtils.HandleException(ex, nameof(DeleteRangeAsync));
        }

        return Result.Successful();
    }

    public static async Task<Result> DeleteByIdAsync<TEntity>(this DbContext dbContext, object id, CancellationToken ct = default) where TEntity : class
    {
        try
        {
            var entity = await dbContext.Set<TEntity>().FindAsync(new[] { id }, ct);
            if (entity == null)
            {
                return Result.Error(ResultErrorType.NotFound, "Entity not found for deletion.");
            }

            dbContext.Set<TEntity>().Remove(entity);
            await dbContext.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            return DbUtils.HandleException(ex, nameof(DeleteByIdAsync));
        }

        return Result.Successful();
    }

    public static async Task<Result> SetActiveStatusAsync<TEntity>(this DbContext dbContext, TEntity entity, bool isActive, CancellationToken ct = default)
        where TEntity : class, ISoftDeletable
    {
        try
        {
            entity.IsActive = isActive;
            return await dbContext.UpdateEntityAsync(entity, ct);
        }
        catch (Exception ex)
        {
            return DbUtils.HandleException(ex, nameof(SetActiveStatusAsync));
        }
    }

    public static async Task<Result> SetActiveStatusRangeAsync<TEntity>(this DbContext dbContext, IEnumerable<TEntity> entities, bool isActive, CancellationToken ct = default)
        where TEntity : class, ISoftDeletable
    {
        try
        {
            foreach (var entity in entities)
            {
                entity.IsActive = isActive;
            }

            return await dbContext.UpdateRangeAsync(entities, ct);
        }
        catch (Exception ex)
        {
            return DbUtils.HandleException(ex, nameof(SetActiveStatusRangeAsync));
        }
    }
}