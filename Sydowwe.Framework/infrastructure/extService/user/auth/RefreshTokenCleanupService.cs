using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sydowwe.Framework.domain.extServiceContract.user.auth;

namespace Sydowwe.Framework.infrastructure.extService.user.auth;

/// <summary>
/// Periodically deletes refresh tokens that have expired. Lives here rather than in a host because it
/// names nothing host-specific — it resolves <see cref="IRefreshTokenService"/>, whose implementation
/// and entity are both Framework's. Register it with <c>services.AddHostedService&lt;…&gt;()</c>.
///
/// <para>Expired tokens are dead credentials that stay linked to a user id, so leaving them is both a
/// growing table and a GDPR Art. 5(1)(e) storage-limitation problem — see CLAUDE.md "Ledger
/// Retention" for the same argument applied to the append-only ledgers.</para>
///
/// <para>The first sweep runs at startup, not after the first interval: a process that restarts more
/// often than <see cref="Interval"/> would otherwise never clean up at all.</para>
///
/// <para>No PII in the logs here — only counts. Do not add the address or id of any token owner.</para>
/// </summary>
public class RefreshTokenCleanupService(
    IServiceScopeFactory serviceScopeFactory,
    ILogger<RefreshTokenCleanupService> logger) : BackgroundService
{
    /// <summary>How long to wait between sweeps. Override for a host that wants a different cadence.</summary>
    protected virtual TimeSpan Interval => TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("RefreshTokenCleanupService started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = serviceScopeFactory.CreateScope();
                var refreshTokenService = scope.ServiceProvider.GetRequiredService<IRefreshTokenService>();

                var deletedCount = await refreshTokenService.CleanupExpiredTokensAsync();
                logger.LogInformation("RefreshTokenCleanupService cleaned up {Count} expired tokens", deletedCount);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in RefreshTokenCleanupService");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Shutdown, not a failure — fall out of the loop quietly.
                break;
            }
        }

        logger.LogInformation("RefreshTokenCleanupService stopped");
    }
}