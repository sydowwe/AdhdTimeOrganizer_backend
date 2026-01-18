namespace AdhdTimeOrganizer.infrastructure.persistence.seeder.@interface;

public interface IDefaultDatabaseSeeder : IDatabaseSeeder
{
    /// <summary>
    /// Seeds the database with data.
    /// </summary>
    /// <returns>A task representing the asynchronous seeding operation.</returns>
    Task Seed(bool overrideData);
}