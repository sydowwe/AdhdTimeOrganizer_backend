using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sydowwe.Framework.domain.entity.user;
using Sydowwe.Framework.domain.extServiceContract.user;

namespace Sydowwe.Framework.infrastructure.persistence;

public static class UserDbContextExtensions
{
    public static void BaseWithUserEntitySaveChangesAsync(this DbContext dbContext, ILoggedUserService loggedUserService, ILogger logger)
    {
        if (dbContext.ChangeTracker.Entries<IEntityWithUser>().Any(entry => entry.State == EntityState.Added))
        {
            if (!loggedUserService.IsAuthenticated)
                return;
            try
            {
                var userId = loggedUserService.GetUserId;
                foreach (var entry in dbContext.ChangeTracker.Entries<IEntityWithUser>())
                    // Only FILL IN an unset owner, never overwrite one the caller set deliberately.
                    // Some rows are written on behalf of a user who is not the caller — a refresh
                    // token minted during login is the clearest case: RefreshTokenService knows whose
                    // it is, and stamping the ambient user here silently reassigns the session to the
                    // wrong account.
                    if (entry.State == EntityState.Added && entry.Entity.UserId == 0)
                        entry.Entity.UserId = userId;
            }
            catch (Exception ex)
            {
                logger.LogError("Failed to get logged user ID: {message}", ex.Message);
            }
        }
    }

    /// <summary>
    /// Applies a global query filter to every entity implementing IEntityWithUser so that
    /// queries automatically scope to the current user. Pass excludeTypes to skip entities
    /// that need a combined filter applied separately.
    /// </summary>
    public static void ApplyUserQueryFilters(this ModelBuilder modelBuilder, ILoggedUserService? loggedUserService, IEnumerable<Type>? excludeTypes = null)
    {
        if (loggedUserService == null)
            return;

        var excluded = excludeTypes?.ToHashSet() ?? [];
        var serviceConstant = Expression.Constant(loggedUserService, typeof(ILoggedUserService));
        var isAuthenticated = Expression.Property(serviceConstant, nameof(ILoggedUserService.IsAuthenticated));
        var getUserId = Expression.Property(serviceConstant, nameof(ILoggedUserService.GetUserId));

        foreach (var entityType in modelBuilder.Model.GetEntityTypes()
                     .Where(t => typeof(IEntityWithUser).IsAssignableFrom(t.ClrType) && !excluded.Contains(t.ClrType)))
        {
            var param = Expression.Parameter(entityType.ClrType, "e");
            var userIdProp = Expression.Property(param, nameof(IEntityWithUser.UserId));
            // !isAuthenticated || e.UserId == currentUserId
            var body = Expression.OrElse(
                Expression.Not(isAuthenticated),
                Expression.Equal(userIdProp, getUserId));
            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(Expression.Lambda(body, param));
        }
    }
}