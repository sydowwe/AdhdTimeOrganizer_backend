namespace AdhdTimeOrganizer.infrastructure.persistence.seeder.@interface.manager;

public interface IUserDefaultSeederManager
{
    /// <summary>
    /// Seeds all user default data for a specific user.
    /// </summary>
    /// <param name="userId">The ID of the user to seed data for.</param>
    /// <param name="overrideData">Whether to truncate existing data before seeding.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SeedAllForUserAsync(long userId, bool overrideData = false, CancellationToken ct = default);

    /// <summary>
    /// Seeds all user default data for all existing users.
    /// </summary>
    /// <param name="overrideData">Whether to truncate existing data before seeding.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SeedAllForAllUsersAsync(bool overrideData = false, CancellationToken ct = default);
}
