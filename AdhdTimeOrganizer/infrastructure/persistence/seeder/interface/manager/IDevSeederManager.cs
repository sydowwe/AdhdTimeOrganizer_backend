namespace AdhdTimeOrganizer.infrastructure.persistence.seeder.@interface.manager;

public interface IDevSeederManager
{
    Task SeedAllAsync(bool overrideData = true);
}