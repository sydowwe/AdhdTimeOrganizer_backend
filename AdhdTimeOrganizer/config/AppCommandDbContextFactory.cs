using AdhdTimeOrganizer.infrastructure.persistence;
using DotNetEnv;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AdhdTimeOrganizer.config;

public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        Env.Load();
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql(DatabaseStringsHelper.GetDefaultDatabaseConnectionString)
            .UseSnakeCaseNamingConvention()
            .ReplaceService<IMigrationsSqlGenerator, PartitionedNpgsqlMigrationsSqlGenerator>();

        return new AppDbContext(optionsBuilder.Options, null!, null!);
    }
}