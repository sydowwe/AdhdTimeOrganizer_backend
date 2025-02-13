using AdhdTimeOrganizer.Common.domain.model.entity;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.Common.infrastructure.persistence;

public static class DbContextHelper
{
    public static void BaseSaveChangesAsync(this DbContext dbContext)
    {
        foreach (var entry in dbContext.ChangeTracker.Entries<BaseEntity>())
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

}