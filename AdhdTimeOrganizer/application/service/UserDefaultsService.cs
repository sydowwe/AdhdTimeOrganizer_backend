using AdhdTimeOrganizer.config.dependencyInjection;
using AdhdTimeOrganizer.domain.result;
using AdhdTimeOrganizer.domain.serviceContract;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.infrastructure.persistence.seeder.@interface.manager;

namespace AdhdTimeOrganizer.application.service;

public class UserDefaultsService(
    IUserDefaultSeederManager seederManager,
    ILogger<UserDefaultsService> logger) : IUserDefaultsService, IScopedService
{
    public async Task<Result> CreateDefaultsAsync(long userId, CancellationToken ct = default)
    {
        try
        {
            await seederManager.SeedAllForUserAsync(userId, overrideData: false, ct);
            return Result.Successful();
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to create defaults for new user {UserId}", userId);
            return DbUtils.HandleException(e, nameof(UserDefaultsService));
        }
    }
}
