using AdhdTimeOrganizer.domain.extServiceContract.user.auth;

namespace AdhdTimeOrganizer.infrastructure.extService;

public class RefreshTokenCleanupService(
    IServiceScopeFactory serviceScopeFactory,
    ILogger<RefreshTokenCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("RefreshTokenCleanupService started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Run every 24 hours
                await Task.Delay(TimeSpan.FromHours(24), stoppingToken);

                using var scope = serviceScopeFactory.CreateScope();
                var refreshTokenService = scope.ServiceProvider.GetRequiredService<IRefreshTokenService>();

                var deletedCount = await refreshTokenService.CleanupExpiredTokensAsync();
                logger.LogInformation("RefreshTokenCleanupService cleaned up {Count} expired tokens", deletedCount);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in RefreshTokenCleanupService");
            }
        }

        logger.LogInformation("RefreshTokenCleanupService stopped");
    }
}
