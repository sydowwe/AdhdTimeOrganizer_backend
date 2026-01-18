namespace AdhdTimeOrganizer.infrastructure.persistence.seeder.@interface;

/// <summary>
/// Common interface for all database seeders.
/// Provides a unified contract for seeding operations.
/// </summary>
public interface IDatabaseSeeder
{
    /// <summary>
    /// Gets the name of the seeder for logging purposes.
    /// </summary>
    string SeederName { get; }

    /// <summary>
    /// Gets the execution order priority (lower numbers execute first).
    /// </summary>
    int Order { get; }
}