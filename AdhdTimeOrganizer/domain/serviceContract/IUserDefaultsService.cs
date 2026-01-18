using AdhdTimeOrganizer.domain.result;

namespace AdhdTimeOrganizer.domain.serviceContract;

public interface IUserDefaultsService
{
    Task<Result> CreateDefaultsAsync(long userId, CancellationToken ct = default);
}