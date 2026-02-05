using AdhdTimeOrganizer.infrastructure.persistence;
using DotNetEnv;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AdhdTimeOrganizer.config;

public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        Env.Load();
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql(DatabaseStringsHelper.GetDefaultDatabaseConnectionString).UseSnakeCaseNamingConvention();

        return new AppDbContext(optionsBuilder.Options, null, null);
    }
}