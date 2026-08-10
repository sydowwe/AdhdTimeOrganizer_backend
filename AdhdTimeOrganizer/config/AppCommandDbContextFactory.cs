using AdhdTimeOrganizer.infrastructure.persistence;
using DotNetEnv;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Migrations;
using Sydowwe.Framework.infrastructure.persistence;

namespace AdhdTimeOrganizer.config;

public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        Env.Load();
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql(DatabaseStringsHelper.GetDefaultDatabaseConnectionString,
                b => b.MigrationsAssembly(typeof(Program).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .ReplaceService<IMigrationsSqlGenerator, PartitionedNpgsqlMigrationsSqlGenerator>();

        return new AppDbContext(optionsBuilder.Options, null!, null!);
    }
}