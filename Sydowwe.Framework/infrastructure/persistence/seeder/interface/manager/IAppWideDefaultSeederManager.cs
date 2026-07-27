namespace Sydowwe.Framework.infrastructure.persistence.seeder.@interface.manager;

/// <summary>
/// Runs the <see cref="IAppWideDefaultSeeder"/>s — production data every environment needs.
/// Safe to call on every startup.
/// </summary>
public interface IAppWideDefaultSeederManager
{
    /// <summary>Runs every app-wide default seeder in ascending <c>Order</c>.</summary>
    /// <param name="overrideData">Update already-existing rows in place. Never truncates.</param>
    Task SeedAllAsync(bool overrideData = false);

    /// <summary>Runs one seeder by <c>SeederName</c>.</summary>
    /// <exception cref="InvalidOperationException">No seeder with that name is registered.</exception>
    Task SeedAsync(string seederName, bool overrideData = false);

    IEnumerable<string> GetAllSeederNames();
}
