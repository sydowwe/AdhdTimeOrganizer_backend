using Sydowwe.Framework.domain.serviceContract;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.domain.result;
using Sydowwe.Framework.infrastructure.persistence;
using Sydowwe.Framework.infrastructure.persistence.seeder.@interface.manager;

namespace AdhdTimeOrganizer.application.service;

public class UserDefaultsService(
    IPerUserDefaultSeederManager seederManager,
    ILogger<UserDefaultsService> logger) : IUserDefaultsService, IScopedService
{
    public async Task<Result> CreateDefaultsAsync(long userId, CancellationToken ct = default)
    {
        try
        {
            await seederManager.SeedAllForUserAsync(userId, false, ct);
            return Result.Successful();
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to create defaults for new user {UserId}", userId);
            return DbUtils.HandleException(e, nameof(UserDefaultsService));
        }
    }
}