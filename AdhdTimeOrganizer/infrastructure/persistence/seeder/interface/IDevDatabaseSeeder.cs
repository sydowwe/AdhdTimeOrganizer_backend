using AdhdTimeOrganizer.domain.helper;
using AdhdTimeOrganizer.domain.model.entityInterface;
using AdhdTimeOrganizer.infrastructure.persistence.seeders;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.infrastructure.persistence.seeder;

public interface IDevDatabaseSeeder : IDatabaseSeeder
{
    public Task SeedForUser(long userId);
    public Task TruncateTable();
}