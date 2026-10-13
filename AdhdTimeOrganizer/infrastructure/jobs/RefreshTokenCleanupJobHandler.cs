using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.Contracts.scheduling;
using Sydowwe.Framework.domain.extServiceContract.user.auth;

namespace AdhdTimeOrganizer.infrastructure.jobs;

/// <summary>
/// Deletes dead refresh tokens — revoked, or expired, and more than 30 days old (the rule is
/// <see cref="IRefreshTokenService.CleanupExpiredTokensAsync"/>'s). They are credentials that stay linked to a user id,
/// so keeping them is a GDPR storage-limitation problem as well as a growing table.
/// <para>
/// Replaces the framework's <c>RefreshTokenCleanupService</c> loop in this host, which ran outside the Scheduler: no run
/// log, no failure or overdue alert, a 24-hour timer that restarted on every boot, and one sweep per instance. That
/// service stays in the framework for hosts without the Scheduler. The handler lives here, not beside it, because
/// <c>Sydowwe.Framework</c> cannot see <c>IScheduledJobHandler</c> — Contracts references the framework, not the reverse.
/// </para>
/// <para>No log line of its own: the service logs the count, and never a token owner.</para>
/// </summary>
public class RefreshTokenCleanupJobHandler(IRefreshTokenService refreshTokenService) : IScheduledJobHandler, IScopedService
{
    public const string HandlerKey = "Portal.RefreshTokenCleanup";

    public string Key => HandlerKey;

    public Task ExecuteAsync(ScheduledJobContext context, CancellationToken ct) =>
        refreshTokenService.CleanupExpiredTokensAsync();
}
