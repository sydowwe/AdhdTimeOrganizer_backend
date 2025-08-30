namespace AdhdTimeOrganizer.infrastructure.persistence.seeder.manager;

public interface IDefaultSeederManager
{
    Task SeedAllAsync(bool overrideData = true);
    Task SeedAsync(string seederName);
    IEnumerable<string> GetAllSeederNames();
}