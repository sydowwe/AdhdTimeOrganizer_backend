namespace Sydowwe.Framework.infrastructure.persistence.seeder.@interface.manager;

/// <summary>
/// Runs the <see cref="IPerUserDefaultSeeder"/>s — the baseline rows an account needs to be usable.
/// <see cref="SeedAllForUserAsync"/> is the sign-up path; the rest are for replaying defaults after
/// their definitions change.
/// </summary>
public interface IPerUserDefaultSeederManager
{
    /// <summary>Runs every per-user default seeder for one user, in ascending <c>Order</c>.</summary>
    /// <param name="overrideData">
    /// Rewrite the user's existing default rows in place first (keeping their ids). Never truncates.
    /// </param>
    Task SeedAllForUserAsync(long userId, bool overrideData = false, CancellationToken ct = default);

    /// <summary>
    /// Same, for every user the <see cref="ISeedUserProvider"/> reports. Sequential — this is a
    /// maintenance/startup operation, not a request path.
    /// </summary>
    Task SeedAllForAllUsersAsync(bool overrideData = false, CancellationToken ct = default);

    /// <summary>Runs one seeder by <c>SeederName</c> for one user.</summary>
    /// <exception cref="InvalidOperationException">No seeder with that name is registered.</exception>
    Task SeedForUserAsync(string seederName, long userId, bool overrideData = false, CancellationToken ct = default);

    IEnumerable<string> GetAllSeederNames();
}