using AdhdTimeOrganizer.application.dto.request.user;
using AdhdTimeOrganizer.application.dto.response.user;
using AdhdTimeOrganizer.domain.extServiceContract.user.auth;
using AdhdTimeOrganizer.domain.model.entity.user;
using FastEndpoints;
using Microsoft.AspNetCore.Identity;

namespace AdhdTimeOrganizer.application.endpoint.user.command.auth.extension;

public class ExtensionLoginEndpoint(
    SignInManager<User> signInManager,
    UserManager<User> userManager,
    IJwtService jwtService,
    IGoogleRecaptchaService googleRecaptchaService)
    : Endpoint<ExtensionLoginRequest, ExtensionLoginResponse>
{
    public override void Configure()
    {
        Post("auth/extension/login");
        AllowAnonymous();
        Throttle(hitLimit: 5, durationSeconds: 60, headerName: "X-Client-Id");
        Summary(s => { s.Summary = "Login for browser extension clients"; });
    }

    public override async Task HandleAsync(ExtensionLoginRequest req, CancellationToken ct)
    {
        var recaptchaResult = await googleRecaptchaService.VerifyRecaptchaAsync(req.RecaptchaToken, "extension_login");
        if (recaptchaResult.Failed)
        {
            AddError("Recaptcha verification failed.");
            await SendErrorsAsync(400, ct);
            return;
        }

        var user = await userManager.FindByEmailAsync(req.Email);
        if (user is null)
        {
            AddError("Invalid email or password");
            await SendErrorsAsync(401, ct);
            return;
        }

        // Extension access check
        if (!user.HasExtensionAccess)
        {
            AddError("Extension access not enabled for this account");
            await SendErrorsAsync(403, ct);
            return;
        }

        if (!user.EmailConfirmed)
        {
            AddError("Email not confirmed");
            await SendErrorsAsync(403, ct);
            return;
        }

        var result = await signInManager.CheckPasswordSignInAsync(user, req.Password, lockoutOnFailure: true);

        if (result.IsLockedOut)
        {
            var lockoutDuration = user.LockoutEnd!.Value - DateTimeOffset.Now;
            var minutes = (int)lockoutDuration.TotalMinutes;
            var seconds = lockoutDuration.Seconds;
            AddError($"Too many failed login attempts. Try again in {minutes}m {seconds}s");
            await SendErrorsAsync(403, ct);
            return;
        }

        if (user.TwoFactorEnabled)
        {
            var response = new ExtensionLoginResponse
            {
                RequiresTwoFactor = true,
                Email = user.Email!
            };
            await SendOkAsync(response, ct);
            return;
        }

        if (!result.Succeeded)
        {
            AddError("Invalid email or password");
            await SendErrorsAsync(401, ct);
            return;
        }

        // Assign ActivityTracking role if not present
        var roles = await userManager.GetRolesAsync(user);
        if (!roles.Contains("ActivityTracking"))
        {
            await userManager.AddToRoleAsync(user, "ActivityTracking");
        }

        // Generate tokens for extension
        var (accessToken, refreshToken) = await jwtService.GenerateTokensForExtensionAsync(
            AuthMethodEnum.Password, user);

        var successResponse = new ExtensionLoginResponse
        {
            RequiresTwoFactor = false,
            Email = user.Email!,
            AccessToken = accessToken,
            RefreshToken = refreshToken
        };

        await SendOkAsync(successResponse, ct);
    }
}
