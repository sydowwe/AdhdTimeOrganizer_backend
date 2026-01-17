namespace AdhdTimeOrganizer.infrastructure.persistence.seeder.manager;

public interface IDevSeederManager
{
    Task SeedAllAsync(bool overrideData = true);
}