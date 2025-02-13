using AdhdTimeOrganizer.Command.application.@interface.users;
using AdhdTimeOrganizer.Command.domain.model.entity.user;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AdhdTimeOrganizer.Command.infrastructure.persistence;

public static class UsersDbContextExtensions
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