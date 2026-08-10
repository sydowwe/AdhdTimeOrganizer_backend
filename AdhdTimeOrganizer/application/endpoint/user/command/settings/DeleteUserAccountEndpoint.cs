using AdhdTimeOrganizer.Core.domain.model.entity.user;
using Microsoft.AspNetCore.Identity;
using Sydowwe.Framework.application.endpoint.user.command.settings;
using Sydowwe.Framework.Contracts.gdpr;

namespace AdhdTimeOrganizer.application.endpoint.user.command.settings;

/// <summary>
/// Account deletion, plus the module half of the erasure the DB cascade cannot reach.
/// <para>
/// The host's module FK configuration gives <c>Notification</c>, <c>NotificationPreference</c> and
/// <c>PushSubscription</c> a cascading FK to <c>User</c>, so those three go with the user row. Every other
/// module table keyed to a user is deliberately <b>FK-free</b> — the modules are not allowed to know the
/// host's user type, and recipient ids are validated at registration instead. Nothing collects those:
/// <c>notification_quiet_hours</c>, <c>reminder_kind_preference</c>, <c>reminder_recipient</c>,
/// <c>reminder_occurrence_action</c> and the ids inside <c>reminder_dispatch.recipients_snapshot</c> would
/// simply outlive the account, indefinitely (GDPR Art. 17). An orphaned <c>reminder_recipient</c> is not
/// only a retention problem — it leaves its definition scanning forever for a user who no longer exists.
/// </para>
/// <para>
/// So this fans out over <c>Sydowwe.Framework.Contracts</c>'s <see cref="ISubjectDataEraser"/> seam, which is what both modules
/// ship their erasers for. The fan-out keeps the direction right — the portal never references a module's
/// erasure logic, and a deployment that drops a module resolves a shorter enumerable with no change here.
/// Each eraser knows its own shape: Notifications deletes outright, Reminders pseudonymizes its
/// append-only ledgers (deleting those would re-open occurrences the scanner already sent).
/// </para>
/// </summary>
public class DeleteUserAccountEndpoint(
    UserManager<User> userManager,
    IEnumerable<ISubjectDataEraser> subjectDataErasers,
    ILogger<DeleteUserAccountEndpoint> logger)
    : BaseDeleteUserAccountEndpoint<User>(userManager)
{
    /// <summary>
    /// Runs the erasers before the user row goes, while their rows are still readable.
    /// <para>
    /// Per the <see cref="ISubjectDataEraser"/> contract an eraser mutates the <b>ambient</b> DbContext and
    /// never commits — the caller owns the transaction. Here that caller is
    /// <c>UserManager.DeleteAsync</c>: Identity's store is backed by the same scoped <c>AppDbContext</c>
    /// these erasers resolve, so its <c>SaveChanges</c> commits the erasure and the account deletion as one
    /// unit. A failed delete therefore leaves the pending erasure uncommitted rather than half-applied.
    /// </para>
    /// <para>
    /// Exceptions are deliberately not caught: the contract is fail-loud, and the base aborts the deletion
    /// when this hook throws — a visibly retryable un-erased account beats one believed erased. The erasers
    /// are idempotent, so that retry is safe.
    /// </para>
    /// </summary>
    protected override async Task BeforeDeleteAsync(User user, CancellationToken ct)
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
}