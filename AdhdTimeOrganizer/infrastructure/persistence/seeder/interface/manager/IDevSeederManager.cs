namespace AdhdTimeOrganizer.infrastructure.persistence.seeder.@interface.manager;

public interface IDevSeederManager
{
    Task SeedAllAsync(bool overrideData = true);
    Task<long?> GetRootAdminUserId();
    Task SeedAsync(string seederName, long userId);
}