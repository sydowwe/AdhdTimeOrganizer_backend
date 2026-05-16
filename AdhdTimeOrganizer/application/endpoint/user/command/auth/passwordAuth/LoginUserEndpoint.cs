using AdhdTimeOrganizer.application.dto.request.user;
using AdhdTimeOrganizer.application.dto.response.user;
using AdhdTimeOrganizer.domain.extServiceContract.user.auth;
using AdhdTimeOrganizer.domain.model.entity.user;
using FastEndpoints;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;

namespace AdhdTimeOrganizer.application.endpoint.user.command.auth.passwordAuth;

public class LoginUserEndpoint(
    SignInManager<User> signInManager,
    UserManager<User> userManager,
    IJwtService jwtService,
    IGoogleRecaptchaService googleRecaptchaService,
    IDataProtectionProvider dataProtectionProvider)
    : Endpoint<PasswordLoginRequest, LoginResponse>
{
    public override void Configure()
    {
        Post("/auth/login");
        AllowAnonymous();
        Throttle(hitLimit: 5, durationSeconds: 60, headerName: "X-Client-Id");
        Summary(s => { s.Summary = "Login a user"; });
    }

    public override async Task HandleAsync(PasswordLoginRequest req, CancellationToken ct)
    {
        var recaptchaResult = await googleRecaptchaService.VerifyRecaptchaAsync(req.RecaptchaToken, "login");
        if (recaptchaResult.Failed)
        {
            AddError("Recaptcha verification failed.");
            await Send.ErrorsAsync(400, ct);
            return;
        }

        var user = await userManager.FindByEmailAsync(req.Email);
        if (user is null || !user.EmailConfirmed)
        {
            // Generic error to prevent user enumeration
            AddError("Invalid email or password");
            await Send.ErrorsAsync(401, ct);
            return;
        }

        var result = await signInManager.CheckPasswordSignInAsync(user, req.Password, lockoutOnFailure: true);

        if (result.IsLockedOut)
        {
            var lockoutDuration = user.LockoutEnd!.Value - DateTimeOffset.Now;
            var minutes = (int)lockoutDuration.TotalMinutes;
            var seconds = lockoutDuration.Seconds;
            AddError($"Too many failed login attempts. Try again in {minutes}m {seconds}s");
            await Send.ErrorsAsync(403, ct);
            return;
        }

        if (!result.Succeeded)
        {
            // Generic error to prevent user enumeration
            AddError("Invalid email or password");
            await Send.ErrorsAsync(401, ct);
            return;
        }

        if (user.TwoFactorEnabled)
        {
            var protector = dataProtectionProvider.CreateProtector("2fa-pending").ToTimeLimitedDataProtector();
            var pendingToken = protector.Protect(user.Id.ToString(), TimeSpan.FromMinutes(5));

            HttpContext.Response.Cookies.Append("pending-2fa", pendingToken, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Path = "/api/auth/login/2fa",
                MaxAge = TimeSpan.FromMinutes(5),
                IsEssential = true
            });

            await Send.OkAsync(new LoginResponse { RequiresTwoFactor = true, Email = user.Email! }, ct);
            return;
        }

        // Successful login - generate JWT and set cookie
        await jwtService.GenerateJwtAndSetAuthCookie(req.StayLoggedIn, AuthMethodEnum.Password, user, HttpContext);

        await Send.OkAsync(new LoginResponse { RequiresTwoFactor = false, Email = user.Email! }, ct);
    }
}