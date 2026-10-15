using AdhdTimeOrganizer.application.service.user;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.infrastructure.persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.Contracts.scheduling;

namespace AdhdTimeOrganizer.infrastructure.jobs;

/// <summary>
/// P3-07: deletes the accounts whose grace period has passed — every user whose <see cref="User.DeletionScheduledFor"/>
/// is due — through <see cref="IUserAccountDeletionService"/>, the path every deletion takes, so the module rows and the
/// sessions go exactly as they do for an unconfirmed sign-up. See <see cref="IAccountDeletionScheduleService"/> for the
/// grace period itself.
/// <para>
/// The schedule is a column, not a one-shot trigger, so it survives a restart with no boot registrar: a deployment that
/// was down when an account came due deletes it on the first run after. Hourly, so an account goes within the hour after
/// the date its owner was told. <b>No exclusions:</b> the seeded root admin is deleted like anyone else, because its
/// owner asked for it — exactly as the immediate delete treated it before the grace period existed.
/// </para>
/// <para>Logs carry ids and counts only.</para>
/// </summary>
public class AccountDeletionJobHandler(
    AppDbContext dbContext,
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<AccountDeletionJobHandler> logger) : IScheduledJobHandler, IScopedService
{
    public const string HandlerKey = "Portal.AccountDeletions";

    /// <summary>Per run; anything past it waits an hour. Bounds a run after a long outage.</summary>
    public const int BatchSize = 100;

    public string Key => HandlerKey;

    public async Task ExecuteAsync(ScheduledJobContext context, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var dueIds = await dbContext.Users
            .IgnoreQueryFilters()
            .Where(u => u.DeletionScheduledFor != null && u.DeletionScheduledFor <= now)
            .OrderBy(u => u.DeletionScheduledFor)
            .Select(u => u.Id)
            .Take(BatchSize)
            .ToListAsync(ct);

        var deleted = 0;
        foreach (var userId in dueIds)
        {
            try
            {
                if (await DeleteAsync(userId, now, ct))
                    deleted++;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // One account that will not go does not keep the rest; the next run retries it.
                logger.LogError(ex, "Deleting scheduled account {UserId} failed", userId);
            }
        }

        if (deleted > 0)
            logger.LogInformation("Scheduled account deletions: deleted {DeletedCount}", deleted);
    }

    /// <summary>
    /// One scope per account: the erasers stage their deletes on the scope's <c>DbContext</c> and only
    /// <c>UserManager.DeleteAsync</c> commits them, so an account whose delete failed must not leave its staged erasure
    /// behind for the next account's save to commit.
    /// </summary>
    private async Task<bool> DeleteAsync(long userId, DateTime now, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var scopedUserManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();

        // Re-read in the scope that deletes it: kept since the query, it stays. A keep landing between this read and the
        // delete is lost — it is a matter of milliseconds past a date the user was told twice, and the alternative is a
        // lock on the user row for the length of every module's erasure.
        var user = await scopedUserManager.FindByIdAsync(userId.ToString());
        if (user is not { DeletionScheduledFor: { } deletesAt } || deletesAt > now)
            return false;

        var result = await scope.ServiceProvider.GetRequiredService<IUserAccountDeletionService>().DeleteAsync(user, ct);
        if (result.Succeeded)
            return true;

        // Codes only: an Identity error's description can interpolate the user name, which is the address.
        logger.LogWarning(
            "Deleting scheduled account {UserId} was refused: {IdentityErrorCodes}",
            userId, string.Join("; ", result.Errors.Select(e => e.Code)));
        return false;
    }
}
