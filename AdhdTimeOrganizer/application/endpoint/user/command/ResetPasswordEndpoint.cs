using AdhdTimeOrganizer.application.dto.request.user;
using AdhdTimeOrganizer.domain.model.entity.user;
using FastEndpoints;
using Microsoft.AspNetCore.Identity;

namespace AdhdTimeOrganizer.application.endpoint.user.command;

public class ResetPasswordEndpoint(UserManager<User> userManager)
    : Endpoint<ResetPasswordRequest, EmptyResponse>
{
    public override void Configure()
    {
        Post("user/reset-password");
        AllowAnonymous();
        Throttle(hitLimit: 5, durationSeconds: 60, headerName: "X-Client-Id");
        Summary(s => { s.Summary = "Reset password using reset token"; });
    }

    public override async Task HandleAsync(ResetPasswordRequest req, CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(req.UserId.ToString());
        if (user is null)
        {
            AddError("Invalid or expired reset token");
            await SendErrorsAsync(400, ct);
            return;
        }

        var result = await userManager.ResetPasswordAsync(user, req.Token, req.NewPassword);
        
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                AddError(error.Description);
            }
            await SendErrorsAsync(400, ct);
            return;
        }

        await SendNoContentAsync(ct);
    }
}
