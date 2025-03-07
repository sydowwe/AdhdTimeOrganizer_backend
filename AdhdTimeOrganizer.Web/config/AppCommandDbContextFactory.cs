using AdhdTimeOrganizer.Command.infrastructure.persistence;
using AdhdTimeOrganizer.Common.infrastructure.persistence;
using DotNetEnv;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AdhdTimeOrganizer.Web.config;

public class AppCommandDbContextFactory : IDesignTimeDbContextFactory<AppCommandDbContext>
{
    public AppCommandDbContext CreateDbContext(string[] args)
    {
        Env.Load();
        var optionsBuilder = new DbContextOptionsBuilder<AppCommandDbContext>();
        optionsBuilder.UseNpgsql(MainHelper.GetCommandDatabaseConnectionString);

        return new AppCommandDbContext(optionsBuilder.Options, null, null);
    }
}