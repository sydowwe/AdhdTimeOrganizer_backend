using AdhdTimeOrganizer.domain.result;

namespace AdhdTimeOrganizer.infrastructure.persistence.seeder;

public interface IUserDefaultsService
{
    Task<Result> CreateDefaultsAsync(long userId, CancellationToken ct = default);
}