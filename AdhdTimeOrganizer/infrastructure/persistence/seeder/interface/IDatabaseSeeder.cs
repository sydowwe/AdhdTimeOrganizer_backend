namespace AdhdTimeOrganizer.infrastructure.persistence.seeder;

/// <summary>
/// Common interface for all database seeders.
/// Provides a unified contract for seeding operations.
/// </summary>
public interface IDatabaseSeeder
{
    Task TruncateTable();

    /// <summary>
    /// Seeds the database with data.
    /// </summary>
    /// <param name="dbContext">The database context to use for seeding.</param>
    /// <returns>A task representing the asynchronous seeding operation.</returns>
    Task Seed();

    /// <summary>
    /// Gets the name of the seeder for logging purposes.
    /// </summary>
    string SeederName { get; }

    /// <summary>
    /// Gets the execution order priority (lower numbers execute first).
    /// </summary>
    int Order { get; }
}