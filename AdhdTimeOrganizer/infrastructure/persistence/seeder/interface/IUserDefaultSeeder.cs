namespace AdhdTimeOrganizer.infrastructure.persistence.seeder.@interface;

/// <summary>
/// Interface for seeders that create default data for individual users.
/// These seeders are called when a new user is created or when regenerating defaults.
/// </summary>
public interface IUserDefaultSeeder : IDatabaseSeeder
{
    /// <summary>
    /// Seeds default data for a specific user.
    /// </summary>
    /// <param name="userId">The ID of the user to seed data for.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous seeding operation.</returns>
    Task SetupDefaults(long userId, CancellationToken ct = default);

    /// <summary>
    /// Resets default data for a specific user.
    /// </summary>
    /// <param name="userId">The ID of the user to seed data for.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous seeding operation.</returns>
    Task<bool> ResetDefaults(long userId, CancellationToken ct = default);
}
