using AdhdTimeOrganizer.application.service.user;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.infrastructure.persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.Contracts.scheduling;
using Sydowwe.Framework.domain.extServiceContract.user;
using Sydowwe.Framework.infrastructure.persistence.seeder;

namespace AdhdTimeOrganizer.infrastructure.jobs;

/// <summary>
/// Sign-ups that never confirmed their address (P2-05): remind once, then purge.
/// <para>
/// <c>RequireConfirmedEmail</c> means an unconfirmed account <b>cannot sign in</b>, so it can never be used — yet it kept
/// an email address, and whatever the per-user defaults created at registration, forever. That is a GDPR
/// storage-limitation problem, not clutter.
/// </para>
/// <list type="number">
/// <item><b>After <see cref="RemindAfter"/>, once:</b> the confirmation link again, built exactly as
/// <c>BaseResendConfirmationEmailEndpoint</c> builds it, marked in <see cref="User.ConfirmationReminderSentAt"/> once it
/// has gone out. Transactional mail — the person asked for the account — so it is not a notification and has no
/// preference to mute it. The registration mail's token has expired by then (Identity's one-day default), so this one
/// carries a fresh link rather than repeating a dead one.</item>
/// <item><b>After <see cref="PurgeAfter"/>:</b> deleted through <see cref="IUserAccountDeletionService"/>, the path a
/// self-deletion takes, so the per-user defaults and the module rows go exactly as they do then. Not conditional on the
/// reminder having gone out: an account that is due is due, and a mail outage must not keep an address forever.</item>
/// </list>
/// <para>
/// <b>Never touched:</b> a confirmed account; a Google sign-in (created confirmed — Google vouched for the address — and
/// excluded by its link as well, in case that ever changes); the seeded root admin. Every other seeded and E2E user is
/// created confirmed.
/// </para>
/// <para>
/// The registration instant is <c>BaseUser.CreatedTimestamp</c>, stamped on insert by <c>DbContextHelper</c> (with a
/// <c>now()</c> database default behind it). Hourly rather than daily so the reminder lands within an hour of its day,
/// not anywhere in the next one; the query is a scan of the unconfirmed few. Logs carry ids and counts only.
/// </para>
/// </summary>
public class UnconfirmedAccountJobHandler(
    AppDbContext dbContext,
    UserManager<User> userManager,
    IUserEmailSenderService<User> emailSender,
    ISeedUserProvider seedUserProvider,
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<UnconfirmedAccountJobHandler> logger) : IScheduledJobHandler, IScopedService
{
    public const string HandlerKey = "Portal.UnconfirmedAccounts";

    /// <summary>How long after sign-up the one reminder goes out.</summary>
    public static readonly TimeSpan RemindAfter = TimeSpan.FromDays(1);

    /// <summary>
    /// How long after sign-up an account still unconfirmed is deleted. The SPA tells the user this deadline and cannot read
    /// it from here: change <c>unconfirmedAccountDeletionDays</c> in the frontend's <c>src/main.ts</c> with it.
    /// </summary>
    public static readonly TimeSpan PurgeAfter = TimeSpan.FromDays(14);

    /// <summary>Per run and per step; anything past it waits an hour. Bounds a run after a long outage.</summary>
    public const int BatchSize = 100;

    public string Key => HandlerKey;

    public async Task ExecuteAsync(ScheduledJobContext context, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        // 0 is never a real id, so "no root admin" excludes nobody.
        var rootAdminId = await seedUserProvider.GetRootAdminUserIdAsync(ct) ?? 0;

        var reminded = await RemindAsync(now, rootAdminId, ct);
        var purged = await PurgeAsync(now, rootAdminId, ct);

        if (reminded > 0 || purged > 0)
            logger.LogInformation(
                "Unconfirmed accounts: reminded {RemindedCount}, purged {PurgedCount}", reminded, purged);
    }

    private IQueryable<User> Unconfirmed(long rootAdminId) =>
        dbContext.Users.Where(u => !u.EmailConfirmed && u.GoogleOAuthUserId == null && u.Id != rootAdminId);

    private async Task<int> RemindAsync(DateTime now, long rootAdminId, CancellationToken ct)
    {
        var signedUpBy = now - RemindAfter;
        var purgeCutoff = now - PurgeAfter;

        // Younger than the purge age as well: an account already due for deletion gets deleted, not a mail first.
        var due = await Unconfirmed(rootAdminId)
            .AsNoTracking()
            .Where(u => u.ConfirmationReminderSentAt == null
                        && u.CreatedTimestamp <= signedUpBy
                        && u.CreatedTimestamp > purgeCutoff)
            .OrderBy(u => u.Id)
            .Take(BatchSize)
            .ToListAsync(ct);

        var reminded = 0;
        foreach (var user in due)
        {
            try
            {
                var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
                await emailSender.SendConfirmationLinkAsync(user, token);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Unmarked, so the next run retries. The id only — never the address.
                logger.LogWarning(ex, "Confirmation reminder failed for user {UserId}", user.Id);
                continue;
            }

            // Marked the moment it went out, and with ExecuteUpdate, never a tracked User: the mark is not an edit, so it
            // must not bump the concurrency stamp a confirmation in flight is about to compare against.
            await dbContext.Users
                .Where(u => u.Id == user.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.ConfirmationReminderSentAt, now), ct);
            reminded++;
        }

        return reminded;
    }

    private async Task<int> PurgeAsync(DateTime now, long rootAdminId, CancellationToken ct)
    {
        var purgeCutoff = now - PurgeAfter;
        var dueIds = await Unconfirmed(rootAdminId)
            .Where(u => u.CreatedTimestamp <= purgeCutoff)
            .OrderBy(u => u.Id)
            .Select(u => u.Id)
            .Take(BatchSize)
            .ToListAsync(ct);

        var purged = 0;
        foreach (var userId in dueIds)
        {
            try
            {
                if (await DeleteAsync(userId, ct))
                    purged++;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // One account that will not go does not keep the rest; the next run retries it.
                logger.LogError(ex, "Purging unconfirmed account {UserId} failed", userId);
            }
        }

        return purged;
    }

    /// <summary>
    /// One scope per account: the erasers stage their deletes on the scope's <c>DbContext</c> and only
    /// <c>UserManager.DeleteAsync</c> commits them, so an account whose delete failed must not leave its staged erasure
    /// behind for the next account's save to commit.
    /// </summary>
    private async Task<bool> DeleteAsync(long userId, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var scopedUserManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();

        // Re-read in the scope that deletes it: confirmed since the query, it stays. A confirmation landing between this
        // read and the delete changes the row version, and Identity reports that as a failed result.
        var user = await scopedUserManager.FindByIdAsync(userId.ToString());
        if (user is null || user.EmailConfirmed)
            return false;

        var result = await scope.ServiceProvider.GetRequiredService<IUserAccountDeletionService>().DeleteAsync(user, ct);
        if (result.Succeeded)
            return true;

        // Codes only: an Identity error's description can interpolate the user name, which is the address.
        logger.LogWarning(
            "Purging unconfirmed account {UserId} was refused: {IdentityErrorCodes}",
            userId, string.Join("; ", result.Errors.Select(e => e.Code)));
        return false;
    }
}
