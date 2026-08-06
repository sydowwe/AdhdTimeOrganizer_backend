using Microsoft.EntityFrameworkCore;

namespace Sydowwe.Framework.infrastructure.persistence.seeder;

public static class SeederExtensions
{
    extension(DbContext context)
    {
        public async Task TruncateTableAsync<TEntity>()
            where TEntity : class
        {
            var entityType = context.Model.FindEntityType(typeof(TEntity));
            var tableName = entityType?.GetTableName();

            if (!string.IsNullOrEmpty(tableName))
            {
                // PostgreSQL TRUNCATE with RESTART IDENTITY to reset sequences.
                // tableName comes from the EF model (not user input) and is a SQL
                // identifier, which cannot be parameterized — EF1002 is a false positive here.
#pragma warning disable EF1002
                await context.Database.ExecuteSqlRawAsync($"TRUNCATE TABLE \"{tableName}\" RESTART IDENTITY");
#pragma warning restore EF1002
            }
        }

        public async Task TruncateTableCascadeAsync<TEntity>()
            where TEntity : class
        {
            var entityType = context.Model.FindEntityType(typeof(TEntity));
            var tableName = entityType?.GetTableName();

            if (!string.IsNullOrEmpty(tableName))
            {
                // PostgreSQL TRUNCATE with RESTART IDENTITY to reset sequences.
                // tableName comes from the EF model (not user input) and is a SQL
                // identifier, which cannot be parameterized — EF1002 is a false positive here.
#pragma warning disable EF1002
                await context.Database.ExecuteSqlRawAsync($"TRUNCATE TABLE \"{tableName}\" RESTART IDENTITY CASCADE");
#pragma warning restore EF1002
            }
        }
    }
}