using AdhdTimeOrganizer.domain.extServiceContract.user;
using AdhdTimeOrganizer.domain.model.entity.user;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.infrastructure.persistence.extensions;

public static class UserDbContextExtensions
{
    public static void BaseWithUserEntitySaveChangesAsync(this DbContext dbContext, ILoggedUserService loggedUserService, ILogger<AppCommandDbContext> logger)
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
}