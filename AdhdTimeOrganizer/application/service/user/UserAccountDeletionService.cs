using AdhdTimeOrganizer.Core.domain.model.entity.user;
using Microsoft.AspNetCore.Identity;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.Contracts.gdpr;

namespace AdhdTimeOrganizer.application.service.user;

/// <summary>
/// Deleting an account — the one path every deletion takes, whoever asks for it: the user themself
/// (<c>DeleteUserAccountEndpoint</c>) or the purge of a sign-up that was never confirmed (<c>UnconfirmedAccountJobHandler</c>).
/// <para>
/// The DB cascade is only half of it. The host's module FK configuration gives <c>Notification</c>,
/// <c>NotificationPreference</c> and <c>PushSubscription</c> a cascading FK to <c>User</c>, so those three go with the
/// user row. Every other module table keyed to a user is deliberately <b>FK-free</b> — the modules are not allowed to
/// know the host's user type, and recipient ids are validated at registration instead. Nothing collects those:
/// <c>notification_quiet_hours</c>, <c>reminder_kind_preference</c>, <c>reminder_recipient</c>,
/// <c>reminder_occurrence_action</c> and the ids inside <c>reminder_dispatch.recipients_snapshot</c> would simply
/// outlive the account, indefinitely (GDPR Art. 17). An orphaned <c>reminder_recipient</c> is not only a retention
/// problem — it leaves its definition scanning forever for a user who no longer exists.
/// </para>
/// <para>
/// So this fans out over <c>Sydowwe.Framework.Contracts</c>'s <see cref="ISubjectDataEraser"/> seam, which is what both
/// modules ship their erasers for. The fan-out keeps the direction right — the portal never references a module's
/// erasure logic, and a deployment that drops a module resolves a shorter enumerable with no change here. Each eraser
/// knows its own shape: Notifications deletes outright, Reminders pseudonymizes its append-only ledgers (deleting those
/// would re-open occurrences the scanner already sent).
/// </para>
/// </summary>
public interface IUserAccountDeletionService
{
    /// <summary>
    /// Stages every module's erasure on the ambient <c>DbContext</c> without committing it — the half of
    /// <see cref="DeleteAsync"/> the endpoint needs on its own, because the framework's
    /// <c>BaseDeleteUserAccountEndpoint</c> owns the <c>UserManager.DeleteAsync</c> call (and the session revocation
    /// around it). Run it while the user row still exists.
    /// </summary>
    Task StageModuleErasureAsync(User user, CancellationToken ct);

    /// <summary>
    /// <see cref="StageModuleErasureAsync"/>, then <c>UserManager.DeleteAsync</c>, which commits both as one unit. A
    /// failed result leaves the staged erasure uncommitted on the scope's <c>DbContext</c>, so a caller deleting more
    /// than one account must give each its own scope.
    /// </summary>
    Task<IdentityResult> DeleteAsync(User user, CancellationToken ct);
}

/// <inheritdoc cref="IUserAccountDeletionService"/>
public class UserAccountDeletionService(
    UserManager<User> userManager,
    IEnumerable<ISubjectDataEraser> subjectDataErasers,
    ILogger<UserAccountDeletionService> logger) : IUserAccountDeletionService, IScopedService
{
    /// <summary>
    /// Per the <see cref="ISubjectDataEraser"/> contract an eraser mutates the <b>ambient</b> DbContext and never
    /// commits — the caller owns the transaction. Here that caller is <c>UserManager.DeleteAsync</c>: Identity's store
    /// is backed by the same scoped <c>AppDbContext</c> these erasers resolve, so its <c>SaveChanges</c> commits the
    /// erasure and the account deletion as one unit. A failed delete therefore leaves the pending erasure uncommitted
    /// rather than half-applied.
    /// <para>
    /// Exceptions are deliberately not caught: the contract is fail-loud, and both callers abort the deletion when this
    /// throws — a visibly retryable un-erased account beats one believed erased. The erasers are idempotent, so that
    /// retry is safe.
    /// </para>
    /// </summary>
    public async Task StageModuleErasureAsync(User user, CancellationToken ct)
    {
        foreach (var eraser in subjectDataErasers)
        {
            // SubjectErasureRequest carries an employee id and a login user id because the solution it was
            // written for erases by both. This portal has no employee concept — the user id is the only
            // identity, and it is what every recipient-side row is keyed by.
            var rows = await eraser.EraseSubjectDataAsync(new SubjectErasureRequest(user.Id, user.Id), ct);

            if (rows > 0)
                logger.LogInformation(
                    "Account deletion for user {UserId}: {Module} staged {Rows} row(s) for erasure",
                    user.Id, eraser.ModuleKey, rows);
        }
    }

    public async Task<IdentityResult> DeleteAsync(User user, CancellationToken ct)
    {
        await StageModuleErasureAsync(user, ct);
        return await userManager.DeleteAsync(user);
    }
}
