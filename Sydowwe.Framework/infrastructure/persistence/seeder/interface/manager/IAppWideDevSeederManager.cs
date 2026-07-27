using System.Reflection;

namespace Sydowwe.Framework.infrastructure.persistence.seeder.@interface.manager;

/// <summary>
/// Runs the <see cref="IAppWideDevSeeder"/>s — fixtures not scoped to one user. Development only:
/// every entry point here can wipe tables.
/// </summary>
public interface IAppWideDevSeederManager
{
    /// <summary>Runs every app-wide dev seeder in ascending <c>Order</c>.</summary>
    /// <param name="overrideData">Truncate all of their tables first, in reverse <c>Order</c>.</param>
    Task SeedAllAsync(bool overrideData = true);

    /// <summary>
    /// Same, restricted to the seeders defined in one assembly — lets a single module's fixtures be
    /// reseeded without touching the rest of the app.
    /// </summary>
    Task SeedAssemblyAsync(Assembly assembly, bool overrideData = true);

    /// <summary>Runs one seeder by <c>SeederName</c>, without truncating.</summary>
    /// <exception cref="InvalidOperationException">No seeder with that name is registered.</exception>
    Task SeedAsync(string seederName);

    /// <summary>Truncates every app-wide dev seeder's table in reverse <c>Order</c>, seeding nothing.</summary>
    Task TruncateAllTablesAsync();

    IEnumerable<string> GetAllSeederNames();
}
