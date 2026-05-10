using AdhdTimeOrganizer.application.dto.request.user;
using AdhdTimeOrganizer.application.helper;
using AdhdTimeOrganizer.application.preprocessor;
using AdhdTimeOrganizer.domain.model.entity.user;
using FastEndpoints;
using Microsoft.AspNetCore.Identity;

namespace AdhdTimeOrganizer.application.endpoint.user.command.settings;

public class DeleteUserAccountEndpoint(UserManager<User> userManager)
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

        var result = await userManager.DeleteAsync(user);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                AddError(error.Description);
            await Send.ErrorsAsync(400, ct);
            return;
        }

        // DB cascade deletes all refresh tokens; clear cookies client-side
        HttpContext.Response.Cookies.Delete("auth-token", new CookieOptions
        {
            HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, Path = "/"
        });
        HttpContext.Response.Cookies.Delete("refresh-token", new CookieOptions
        {
            HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, Path = "/api/auth"
        });

        await Send.NoContentAsync(ct);
    }
}
