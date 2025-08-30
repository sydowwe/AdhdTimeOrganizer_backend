using AdhdTimeOrganizer.infrastructure.persistence;
using DotNetEnv;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AdhdTimeOrganizer.config;

public class AppCommandDbContextFactory : IDesignTimeDbContextFactory<AppCommandDbContext>
{
    public AppCommandDbContext CreateDbContext(string[] args)
    {
        Env.Load();
        var optionsBuilder = new DbContextOptionsBuilder<AppCommandDbContext>();
        optionsBuilder.UseNpgsql(DatabaseStringsHelper.GetAdminPortalDatabaseConnectionString, b => b.MigrationsAssembly("MojaDigitalnaFirma.Sandbox.AdminPortal"));

        return new AppCommandDbContext(optionsBuilder.Options, null, null);
    }
}