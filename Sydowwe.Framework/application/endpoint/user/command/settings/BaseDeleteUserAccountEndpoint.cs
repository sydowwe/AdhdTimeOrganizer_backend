using FastEndpoints;
using Microsoft.AspNetCore.Identity;
using Sydowwe.Framework.application.dto.request.user;
using Sydowwe.Framework.application.preprocessor;
using Sydowwe.Framework.domain.entity.user;
using Sydowwe.Framework.domain.helper;

namespace Sydowwe.Framework.application.endpoint.user.command.settings;

/// <summary>
/// Permanently deletes the current user's account.
///
/// <para>Gated by <see cref="VerifyUserPreProcessor{TUser,TRequest}"/> exactly like a password
/// change: the caller must re-supply the current password AND a valid 2FA token. An authenticated
/// session alone is deliberately not enough for an irreversible action.</para>
///
/// <para>Refresh tokens go with the user row by DB cascade; the auth cookies are cleared here so the
/// caller is not left holding credentials for an account that no longer exists.</para>
///
/// <para>The two hooks exist because "delete the account" is only the *portal* half of erasure. Real
/// GDPR Art. 17 erasure per solution means work this endpoint cannot know about — external
/// integrations to revoke, ledgers to anonymize, blobs to drop. Both default to no-ops; without them
/// the first solution that needs one forks the base.</para>
///
/// <para>Abstract like every other endpoint in this assembly: the Framework assembly is deliberately
/// excluded from FastEndpoints discovery (<c>o.Assemblies</c> in the host's <c>Program.cs</c>), so a
/// concrete endpoint here would never be routed. Hosts derive a thin subclass instead.</para>
/// </summary>
public abstract class BaseDeleteUserAccountEndpoint<TUser>(UserManager<TUser> userManager)
    : Endpoint<VerifyUserRequest, EmptyResponse>
    where TUser : BaseUser
{
    public override void Configure()
    {
        Delete("user/account");
        PreProcessor<VerifyUserPreProcessor<TUser, VerifyUserRequest>>();
        Summary(s =>
        {
            s.Summary = "Permanently delete user account";
            s.Description = "This action is irreversible. All user data will be deleted.";
        });
    }

    /// <summary>
    /// Runs before the user row is deleted, while it and everything cascading off it still exist.
    /// Use it for cleanup that needs to read the account. Throwing aborts the deletion.
    /// </summary>
    protected virtual Task BeforeDeleteAsync(TUser user, CancellationToken ct) => Task.CompletedTask;

    /// <summary>
    /// Runs after the account is gone, so it gets the id rather than the entity. Use it for cleanup
    /// that only needs the identifier — external revocations, ledger anonymization, blob deletion.
    /// </summary>
    protected virtual Task AfterDeleteAsync(long userId, CancellationToken ct) => Task.CompletedTask;

    public override async Task HandleAsync(VerifyUserRequest req, CancellationToken ct)
    {
        // Verified by the pre-processor — use that instance rather than re-querying, so the account
        // deleted is exactly the one whose identity was proven.
        var user = HttpContext.GetVerifiedUser<TUser>();
        var userId = user.Id;

        await BeforeDeleteAsync(user, ct);

        var result = await userManager.DeleteAsync(user);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                AddError(error.Description);
            await Send.ErrorsAsync(400, ct);
            return;
        }

        await AfterDeleteAsync(userId, ct);

        // DB cascade deletes all refresh tokens; clear cookies client-side
        HttpContext.Response.ClearSessionCookies();

        await Send.NoContentAsync(ct);
    }
}
