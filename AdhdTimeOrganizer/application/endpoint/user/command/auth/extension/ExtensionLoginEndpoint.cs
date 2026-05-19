using AdhdTimeOrganizer.application.dto.request.user;
using AdhdTimeOrganizer.application.dto.response.user;
using AdhdTimeOrganizer.domain.extServiceContract.user.auth;
using AdhdTimeOrganizer.domain.model.entity.user;
using FastEndpoints;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;

namespace AdhdTimeOrganizer.application.endpoint.user.command.auth.extension;

public class ExtensionLoginEndpoint(
    SignInManager<User> signInManager,
    UserManager<User> userManager,
    IJwtService jwtService,
    IDataProtectionProvider dataProtectionProvider)
    : Endpoint<ExtensionLoginRequest, ExtensionLoginResponse>
{
    public override void Configure()
    {
        Post("/auth/extension/login");
        AllowAnonymous();
        Throttle(hitLimit: 5, durationSeconds: 60, headerName: "X-Real-IP");
        Summary(s => { s.Summary = "Login for browser extension clients"; });
    }

    public override async Task HandleAsync(ExtensionLoginRequest req, CancellationToken ct)
    {
        var user = await userManager.FindByEmailAsync(req.Email);
        if (user is null || !user.EmailConfirmed)
        {
            AddError("Invalid email or password");
            await Send.ErrorsAsync(401, ct);
            return;
        }

        // Extension access check
        if (!user.HasExtensionAccess)
        {
            AddError("Extension access not enabled for this account");
            await Send.ErrorsAsync(403, ct);
            return;
        }

        var result = await signInManager.CheckPasswordSignInAsync(user, req.Password, lockoutOnFailure: true);

        if (result.IsLockedOut)
        {
            var lockoutDuration = user.LockoutEnd!.Value - DateTimeOffset.UtcNow;
            var minutes = (int)lockoutDuration.TotalMinutes;
            var seconds = lockoutDuration.Seconds;
            AddError($"Too many failed login attempts. Try again in {minutes}m {seconds}s");
            await Send.ErrorsAsync(403, ct);
            return;
        }

        if (!result.Succeeded)
        {
            AddError("Invalid email or password");
            await Send.ErrorsAsync(401, ct);
            return;
        }

        if (user.TwoFactorEnabled)
        {
            var protector = dataProtectionProvider.CreateProtector("2fa-pending").ToTimeLimitedDataProtector();
            var pendingToken = protector.Protect(user.Id.ToString(), TimeSpan.FromMinutes(5));

            await Send.OkAsync(new ExtensionLoginResponse
            {
                RequiresTwoFactor = true,
                Email = user.Email!,
                PendingAuthToken = pendingToken
            }, ct);
            return;
        }

        // Generate tokens for extension
        var (accessToken, refreshToken) = await jwtService.GenerateTokensForExtensionAsync(
            AuthMethodEnum.Password, user);

        await Send.OkAsync(new ExtensionLoginResponse
        {
            RequiresTwoFactor = false,
            Email = user.Email!,
            AccessToken = accessToken,
            RefreshToken = refreshToken
        }, ct);
    }
}
