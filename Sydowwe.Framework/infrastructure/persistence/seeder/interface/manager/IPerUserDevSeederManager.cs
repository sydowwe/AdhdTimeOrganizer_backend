using System.Reflection;

namespace Sydowwe.Framework.infrastructure.persistence.seeder.@interface.manager;

/// <summary>
/// Runs the <see cref="IPerUserDevSeeder"/>s — per-user fixtures. Development only: every entry
/// point here can wipe tables, and truncation is never scoped to one user, so overriding for one
/// user clears everybody's fixtures.
/// </summary>
public interface IPerUserDevSeederManager
{
    /// <summary>Runs every per-user dev seeder for one user, in ascending <c>Order</c>.</summary>
    /// <param name="overrideData">
    /// Truncate all of their tables first, in reverse <c>Order</c> — <b>all users' rows</b>, not just
    /// this user's.
    /// </param>
    Task SeedAllForUserAsync(long userId, bool overrideData = true);

    /// <summary>
    /// Same, targeting the root admin from <see cref="ISeedUserProvider"/>. No-ops with a warning
    /// when that account does not exist yet — the usual "fresh database, dev seeding on startup" case.
    /// </summary>
    Task SeedAllForRootAdminAsync(bool overrideData = true);

    /// <summary>
    /// Same as <see cref="SeedAllForUserAsync"/>, restricted to the seeders defined in one assembly.
    /// </summary>
    Task SeedAssemblyForUserAsync(Assembly assembly, long userId, bool overrideData = true);

    /// <summary>Runs one seeder by <c>SeederName</c> for one user, without truncating.</summary>
    /// <exception cref="InvalidOperationException">No seeder with that name is registered.</exception>
    Task SeedForUserAsync(string seederName, long userId);

    /// <summary>Truncates every per-user dev seeder's table in reverse <c>Order</c>, seeding nothing.</summary>
    Task TruncateAllTablesAsync();

    IEnumerable<string> GetAllSeederNames();
}