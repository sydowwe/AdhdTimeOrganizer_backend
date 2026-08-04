using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.domain.auth;
using Sydowwe.Framework.domain.entity.user;
using Sydowwe.Framework.domain.result;
using Sydowwe.Framework.domain.serviceContract;

namespace Sydowwe.Framework.application.service.auth;

/// <summary>How account creation ended. Everything except <see cref="Created"/> is terminal and the
/// caller renders it as an error response using <see cref="UserRegistrationResult{TUser}.StatusCode"/>.</summary>
public enum UserRegistrationOutcome
{
    /// <summary>The account, its role, its defaults and any post-create step are all committed.</summary>
    Created,

    /// <summary>Identity rejected the account because the email / username is taken.</summary>
    DuplicateUser,

    /// <summary>Identity rejected the account for any other reason — password policy, invalid email.</summary>
    InvalidRegistration,

    /// <summary>The account was created but the default role could not be assigned. Rolled back.</summary>
    RoleAssignmentFailed,

    /// <summary>The caller's own in-transaction step failed (e.g. 2FA provisioning). Rolled back.</summary>
    PostCreateStepFailed,

    /// <summary>The host's <see cref="IUserDefaultsService"/> failed. Rolled back.</summary>
    DefaultsFailed,

    /// <summary>Anything thrown. Rolled back.</summary>
    Unexpected
}

public sealed record UserRegistrationResult<TUser>(
    UserRegistrationOutcome Outcome,
    TUser? User,
    string? ErrorMessage = null)
    where TUser : BaseUser
{
    public bool Failed => Outcome is not UserRegistrationOutcome.Created;

    /// <summary>
    /// HTTP status for the outcome. Load-bearing and identical across sign-up methods: 409 for a
    /// taken address, 400 for a rejected registration, 500 for every server-side step that failed
    /// after the credentials themselves were accepted.
    /// </summary>
    public int StatusCode => Outcome switch
    {
        UserRegistrationOutcome.Created => 200,
        UserRegistrationOutcome.DuplicateUser => 409,
        UserRegistrationOutcome.InvalidRegistration => 400,
        _ => 500
    };
}

/// <summary>
/// The create-account decision, shared by every sign-up method — the password flow
/// (<c>BaseRegisterUserEndpoint</c>) and any federated one (the portal's Google sign-in today,
/// another provider tomorrow). Only how the identity is proven differs between them; what it means
/// to *become a user here* — Identity insert, default role, host defaults, all in one transaction —
/// must not be re-implemented per method, because a second copy is how they drift apart on
/// atomicity or on the status codes.
///
/// <para>Atomicity is the point: every step runs inside one transaction and a failure anywhere rolls
/// the account creation back, so a half-registered account — a user row with no role, or no defaults —
/// can never be left behind for someone to log into.</para>
///
/// <para>Deliberately a plain static helper rather than a DI service, matching
/// <see cref="PasswordSignInFlow"/>: every caller already injects all three dependencies, and an open
/// generic over <c>TUser</c> would need hand-registration per host.</para>
///
/// <para>PII: handles an email and a password. It logs nothing, deliberately — see CLAUDE.md
/// "Logging (no PII at the call site)". Keep it that way.</para>
/// </summary>
public static class UserRegistrationFlow
{
    /// <param name="newUser">The user entity to insert, already populated by the caller's request DTO.</param>
    /// <param name="password">The local password, or <c>null</c> for a federated sign-up that has none.</param>
    /// <param name="afterUserCreated">
    /// Optional step run inside the transaction, after the role is assigned and before the host's
    /// defaults. This is where a caller provisions things that must not survive a rolled-back
    /// registration — 2FA secrets, for instance. A failed <see cref="Result"/> aborts and rolls back.
    /// </param>
    public static async Task<UserRegistrationResult<TUser>> RunAsync<TUser>(
        UserManager<TUser> userManager,
        DbContext dbContext,
        IUserDefaultsService userDefaultsService,
        TUser newUser,
        string? password = null,
        Func<TUser, CancellationToken, Task<Result>>? afterUserCreated = null,
        CancellationToken ct = default)
        where TUser : BaseUser
    {
        await using var tx = await dbContext.Database.BeginTransactionAsync(ct);

        // Every failure exit rolls back explicitly rather than leaning on the implicit rollback that
        // disposing an uncommitted transaction performs. Same end state, but atomicity is this method's
        // whole purpose, so it is spelled out instead of inferred from `await using`.
        async Task<UserRegistrationResult<TUser>> Fail(UserRegistrationOutcome outcome, string? message)
        {
            await tx.RollbackAsync(ct);
            return new UserRegistrationResult<TUser>(outcome, null, message);
        }

        try
        {
            var identityResult = password is null
                ? await userManager.CreateAsync(newUser)
                : await userManager.CreateAsync(newUser, password);

            if (!identityResult.Succeeded)
            {
                var duplicate = identityResult.Errors.Any(e => e.Code is "DuplicateUserName" or "DuplicateEmail");
                return await Fail(
                    duplicate ? UserRegistrationOutcome.DuplicateUser : UserRegistrationOutcome.InvalidRegistration,
                    duplicate ? "User already exists" : "Failed to register user: " + Describe(identityResult));
            }

            // The deployment's catalog decides which role a new account gets - the framework has no
            // role names of its own to fall back on.
            identityResult = await userManager.AddToRoleAsync(newUser, FrameworkRoles.Catalog.DefaultRoleName);
            if (!identityResult.Succeeded)
                return await Fail(UserRegistrationOutcome.RoleAssignmentFailed,
                    "Failed to add user to role: " + Describe(identityResult));

            if (afterUserCreated is not null)
            {
                var postCreateResult = await afterUserCreated(newUser, ct);
                if (postCreateResult.Failed)
                    return await Fail(UserRegistrationOutcome.PostCreateStepFailed, postCreateResult.ErrorMessage);
            }

            var defaultsResult = await userDefaultsService.CreateDefaultsAsync(newUser.Id, ct);
            if (defaultsResult.Failed)
                return await Fail(UserRegistrationOutcome.DefaultsFailed, defaultsResult.ErrorMessage);

            await tx.CommitAsync(ct);
            return new UserRegistrationResult<TUser>(UserRegistrationOutcome.Created, newUser);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            return new UserRegistrationResult<TUser>(UserRegistrationOutcome.Unexpected, null, ex.Message);
        }
    }

    private static string Describe(IdentityResult result) =>
        string.Join(", ", result.Errors.Select(e => e.Description));
}
