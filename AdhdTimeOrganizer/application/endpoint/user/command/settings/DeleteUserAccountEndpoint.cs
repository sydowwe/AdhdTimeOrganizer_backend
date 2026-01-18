using AdhdTimeOrganizer.application.dto.request.user;
using AdhdTimeOrganizer.application.helper;
using AdhdTimeOrganizer.application.preprocessor;
using AdhdTimeOrganizer.domain.extServiceContract.user.auth;
using AdhdTimeOrganizer.domain.model.entity.user;
using FastEndpoints;
using Microsoft.AspNetCore.Identity;

namespace AdhdTimeOrganizer.application.endpoint.user.command.settings;

/// <summary>
/// Permanently deletes user account. Requires password and 2FA verification.
/// This action is irreversible.
/// </summary>
public class DeleteUserAccountEndpoint(
    UserManager<User> userManager,
    SignInManager<User> signInManager,
    ITwoFactorAuthService twoFactorAuthService)
    : Endpoint<VerifyUserRequest, EmptyResponse>
{
    public override void Configure()
    {
        Delete("user/account");
        PreProcessor<VerifyUserPreProcessor<VerifyUserRequest>>();
        Summary(s => 
        { 
            s.Summary = "Permanently delete user account";
            s.Description = "This action is irreversible. All user data will be deleted.";
        });
    }

    public override async Task HandleAsync(VerifyUserRequest req, CancellationToken ct)
    {
        var user = HttpContext.GetVerifiedUser();

        // Sign out before deletion
        await signInManager.SignOutAsync();

        // Delete user account
        var result = await userManager.DeleteAsync(user);
        
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                AddError(error.Description);
            }
            await SendErrorsAsync(400, ct);
            return;
        }

        // TODO: Consider soft delete instead of hard delete
        // TODO: Add cleanup of related data (user content, files, etc.)
        // TODO: Send confirmation email that account was deleted

        await SendNoContentAsync(ct);
    }
}
