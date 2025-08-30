// using FastEndpoints;
// using Microsoft.AspNetCore.Identity;
// using MojaDigitalnaFirma.AdminPortal.application.dto.request.user;
// using MojaDigitalnaFirma.AdminPortal.application.@interface.users;
// using MojaDigitalnaFirma.AdminPortal.domain.model.entity.user;
//
// namespace MojaDigitalnaFirma.AdminPortal.application.endpoint.user.command.auth;
//
// public class ChangePasswordEndpoint(ILoggedUserService loggedUserService, SignInManager<User> signInManager, UserManager<User> userManager) : Endpoint<ChangePasswordRequest>
// {
//     public override void Configure()
//     {
//         Post("user/change-password");
//         Summary(s => { s.Summary = "Change password as user"; });
//     }
//
//     public override async Task HandleAsync(ChangePasswordRequest request, CancellationToken ct)
//     {
//         var user = await loggedUserService.GetCurrentUserAsync();
//         // var twoFactorAuthResult = await ValidateTwoFactorAuthAsync(user, request.TwoFactorAuthToken);
//         // if (twoFactorAuthResult.Failed) return twoFactorAuthResult;
//
//         var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
//         if (!result.Succeeded)
//         {
//             AddError(result.Errors.ToString() ?? string.Empty);
//             await SendErrorsAsync(400, ct);
//             return;
//         }
//
//         await userManager.UpdateSecurityStampAsync(user);
//         await signInManager.SignOutAsync();
//
//         await SendNoContentAsync(ct);
//     }
// }

