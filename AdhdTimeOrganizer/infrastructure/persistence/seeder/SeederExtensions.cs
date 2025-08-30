using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.infrastructure.persistence.seeders;

public static class SeederExtensions
{
    public static async Task TruncateTableAsync<TEntity>(this DbContext context)
        where TEntity : class
    {
        var entityType = context.Model.FindEntityType(typeof(TEntity));
        var tableName = entityType?.GetTableName();

        if (!string.IsNullOrEmpty(tableName))
        {
            // PostgreSQL TRUNCATE with RESTART IDENTITY to reset sequences
            await context.Database.ExecuteSqlRawAsync($"TRUNCATE TABLE \"{tableName}\" RESTART IDENTITY CASCADE");
        }
    }
}