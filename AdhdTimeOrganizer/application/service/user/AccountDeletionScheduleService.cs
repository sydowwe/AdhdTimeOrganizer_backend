using System.Security.Cryptography;
using System.Text;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.infrastructure.persistence;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.domain.extServiceContract.user;
using Sydowwe.Framework.domain.extServiceContract.user.auth;
using Sydowwe.Notifications.domain.entity;

namespace AdhdTimeOrganizer.application.service.user;

/// <summary>
/// Account deletion with a grace period (P3-07). Deleting an account on impulse is the textbook ADHD regret, so
/// <c>DELETE user/account</c> schedules the deletion <see cref="GracePeriod"/> out instead of performing it, and the
/// account can be kept until then. The deletion itself is <c>AccountDeletionJobHandler</c>'s, through
/// <see cref="IUserAccountDeletionService"/> — the one path every deletion takes.
/// <para>
/// <b>Scheduling</b> stamps <see cref="User.DeletionScheduledFor"/> and the hash of a fresh keep token, then shuts every
/// door that could reach the user during the grace period: every session (trackers included) is revoked, every push
/// subscription is deleted, and the host's <see cref="AccountDeletionSuppressionReader"/> keeps the notification
/// dispatcher off their phone and inbox for as long as the column is set. Then the keep link is mailed.
/// </para>
/// <para>
/// <b>Keeping</b> clears both columns, either from a signed-in web session or with the mailed token. The token is
/// single-use because keeping clears its hash, and it dies with the deletion because a passed schedule is refused.
/// </para>
/// <para>
/// GDPR Art. 17 asks for erasure "without undue delay". A short grace the data subject chose, with the date stated in
/// the confirmation, the mail and the privacy policy, is the usual reading (decided 2026-10-15, P3-07).
/// </para>
/// </summary>
public interface IAccountDeletionScheduleService
{
    /// <summary>
    /// Schedules the deletion and closes every channel to the user, returning the instant the account goes. Asking again
    /// while a deletion is pending never pushes the date out — the earlier one stands — but does mail a fresh link.
    /// </summary>
    Task<DateTime> ScheduleAsync(User user, CancellationToken ct);

    /// <summary>Cancels a pending deletion for a signed-in user. False when nothing was pending.</summary>
    Task<bool> KeepAsync(long userId, CancellationToken ct);

    /// <summary>
    /// Cancels a pending deletion with the mailed token. False — and nothing changes — for an unknown user, a wrong or
    /// already-used token, or a schedule whose moment has passed.
    /// </summary>
    Task<bool> KeepWithTokenAsync(long userId, string token, CancellationToken ct);
}

/// <inheritdoc cref="IAccountDeletionScheduleService"/>
public class AccountDeletionScheduleService(
    AppDbContext dbContext,
    IRefreshTokenService refreshTokenService,
    IUserEmailSenderService<User> emailSender,
    TimeProvider timeProvider,
    ILogger<AccountDeletionScheduleService> logger) : IAccountDeletionScheduleService, IScopedService
{
    /// <summary>
    /// How long a scheduled account lives. Two weeks, the same as an unconfirmed sign-up (P2-05). The SPA states the date
    /// before the user confirms and cannot read it from here: change <c>accountDeletionGraceDays</c> in the frontend's
    /// <c>src/main.ts</c>, and the privacy policy's erasure clause, with it.
    /// </summary>
    public static readonly TimeSpan GracePeriod = TimeSpan.FromDays(14);

    public async Task<DateTime> ScheduleAsync(User user, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var proposed = now + GracePeriod;

        var token = NewToken();
        var tokenHash = Hash(token);

        // The pending date, read fresh rather than off the verified user, so two requests cannot both believe they are first.
        var pending = await dbContext.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.Id == user.Id)
            .Select(u => u.DeletionScheduledFor)
            .SingleAsync(ct);
        var deletesAt = pending is { } earlier && earlier < proposed ? earlier : proposed;

        // ExecuteUpdate, never a tracked save: the verified user was loaded through UserManager, and the schedule is not an
        // edit of the account that should move its concurrency stamp.
        await dbContext.Users.IgnoreQueryFilters()
            .Where(u => u.Id == user.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.DeletionScheduledFor, deletesAt)
                .SetProperty(u => u.DeletionKeepTokenHash, tokenHash), ct);

        // Every session, trackers included, and every one is reported to the revocation cache, so a session whose "live"
        // verdict was cached does not keep authenticating. Signing in again on the web is how the user keeps the account.
        await refreshTokenService.RevokeAllUserTokensAsync(user.Id);

        // Nothing can buzz during the grace period, without any producer having to know about it. The suppression reader
        // covers email and any subscription a web sign-in registers meanwhile; after a keep, the app re-registers this
        // device's subscription on its next start.
        await dbContext.Set<PushSubscription>()
            .Where(s => s.UserId == user.Id)
            .ExecuteDeleteAsync(ct);

        try
        {
            await emailSender.SendAccountDeletionScheduledAsync(user, new DateTimeOffset(deletesAt, TimeSpan.Zero), token);
        }
        catch (Exception ex)
        {
            // The schedule stands: the user asked for it, and signing in still keeps the account. The id only.
            logger.LogWarning(ex, "Account deletion scheduled for user {UserId}, but the keep-account mail failed", user.Id);
        }

        logger.LogInformation("Account deletion scheduled for user {UserId} at {DeletesAt:o}", user.Id, deletesAt);
        return deletesAt;
    }

    public async Task<bool> KeepAsync(long userId, CancellationToken ct)
    {
        var kept = await dbContext.Users.IgnoreQueryFilters()
            .Where(u => u.Id == userId && u.DeletionScheduledFor != null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.DeletionScheduledFor, (DateTime?)null)
                .SetProperty(u => u.DeletionKeepTokenHash, (string?)null), ct) > 0;

        if (kept)
            logger.LogInformation("Account deletion cancelled for user {UserId} from a signed-in session", userId);
        return kept;
    }

    public async Task<bool> KeepWithTokenAsync(long userId, string token, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(token))
            return false;

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var tokenHash = Hash(token);

        // The comparison happens in the WHERE, against a hash: the raw token is never stored, so there is no stored secret
        // to time a comparison against, and matching on the hash is also what makes the keep single-use under a race —
        // the second request finds the hash already cleared.
        var kept = await dbContext.Users.IgnoreQueryFilters()
            .Where(u => u.Id == userId
                        && u.DeletionKeepTokenHash == tokenHash
                        && u.DeletionScheduledFor != null
                        && u.DeletionScheduledFor > now)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.DeletionScheduledFor, (DateTime?)null)
                .SetProperty(u => u.DeletionKeepTokenHash, (string?)null), ct) > 0;

        if (kept)
            logger.LogInformation("Account deletion cancelled for user {UserId} by the mailed link", userId);
        return kept;
    }

    /// <summary>32 random bytes, base64url — the same shape as a refresh token, and as unguessable.</summary>
    private static string NewToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
