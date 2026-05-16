using System.Linq.Expressions;
using AdhdTimeOrganizer.domain.extServiceContract.user;
using AdhdTimeOrganizer.domain.model.entity.user;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.infrastructure.persistence.extensions;

public static class UserDbContextExtensions
{
    public static void BaseWithUserEntitySaveChangesAsync(this DbContext dbContext, ILoggedUserService loggedUserService, ILogger<AppDbContext> logger)
    {
        if (dbContext.ChangeTracker.Entries<BaseEntityWithUser>().Any(entry => entry.State == EntityState.Added))
        {
            if (!loggedUserService.IsAuthenticated) return;
            try
            {
                var userId = loggedUserService.GetUserId;
                foreach (var entry in dbContext.ChangeTracker.Entries<BaseEntityWithUser>())
                    if (entry.State == EntityState.Added)
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
        if (loggedUserService == null) return;

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