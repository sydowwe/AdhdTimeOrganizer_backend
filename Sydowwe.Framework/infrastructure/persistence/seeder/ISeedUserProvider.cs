namespace Sydowwe.Framework.infrastructure.persistence.seeder;

/// <summary>
/// The seam through which seeding code finds users. The framework has no mapped user entity of its
/// own to query — the host owns it — so per-user managers and app-wide dev seeders ask for ids here
/// instead of touching a DbSet.
/// <para>
/// Implement once in the composition root over the host's real user entity, and register it (the
/// framework ships no implementation). Without it registered, every per-user manager entry point
/// that has to discover users for itself throws.
/// </para>
/// </summary>
public interface ISeedUserProvider
{
    /// <summary>
    /// Ids of all users, ascending. Used to replay per-user default seeders across the whole app.
    /// </summary>
    Task<IReadOnlyList<long>> GetAllUserIdsAsync(CancellationToken ct = default);

    /// <summary>
    /// Id of the root admin account that dev fixtures are attached to, or <c>null</c> when it does
    /// not exist yet — in which case dev seeding is skipped rather than failing.
    /// </summary>
    Task<long?> GetRootAdminUserIdAsync(CancellationToken ct = default);

    /// <summary>
    /// The first <paramref name="count"/> user ids, ascending. For fixtures that want a handful of
    /// distinct owners (a couple of recipients, a shared inbox) without caring who they are.
    /// </summary>
    Task<IReadOnlyList<long>> GetSeedUserIdsAsync(int count, CancellationToken ct = default);
}