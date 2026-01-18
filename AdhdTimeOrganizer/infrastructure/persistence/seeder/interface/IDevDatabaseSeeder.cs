namespace AdhdTimeOrganizer.infrastructure.persistence.seeder.@interface;

public interface IDevDatabaseSeeder : IDatabaseSeeder
{
    public Task SeedForUser(long userId);
    public Task TruncateTable();
}