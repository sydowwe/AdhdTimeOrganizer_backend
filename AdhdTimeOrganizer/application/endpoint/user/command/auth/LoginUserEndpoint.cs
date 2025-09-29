using System.Security.Claims;
using AdhdTimeOrganizer.application.dto.request.user;
using AdhdTimeOrganizer.application.dto.response.user;
using AdhdTimeOrganizer.config;
using AdhdTimeOrganizer.domain.extServiceContract.user.auth;
using AdhdTimeOrganizer.domain.model.entity.user;
using AdhdTimeOrganizer.infrastructure.extService.user.auth;
using FastEndpoints;
using Microsoft.AspNetCore.Identity;
using UserMapper = AdhdTimeOrganizer.application.mapper.UserMapper;

namespace AdhdTimeOrganizer.application.endpoint.user.command.auth;

public class LoginEndpoint(SignInManager<User> signInManager, UserManager<User> userManager, IJwtService jwtService, IGoogleRecaptchaService googleRecaptchaService, UserMapper mapper)
    : Endpoint<PasswordLoginRequest, LoginResponse>
{
    public override void Configure()
    {
        Post("user/login");
        AllowAnonymous();
        Throttle(hitLimit: 5, durationSeconds: 60, headerName: "X-Client-Id");
        Summary(s => { s.Summary = "Login a user"; });
    }

    public override async Task HandleAsync(PasswordLoginRequest req, CancellationToken ct)
    {
        var recaptchaResult = await googleRecaptchaService.VerifyRecaptchaAsync(req.RecaptchaToken, "login");
        if (recaptchaResult.Failed)
        {
            AddError("Recaptcha failed.");
            await SendErrorsAsync(403, ct);
            return;
        }

        var user = await userManager.FindByEmailAsync(req.Email);
        if (user is null)
        {
            AddError(r => r.Email, "Email or password is invalid.");
            await SendNotFoundAsync(ct);
            return;
        }

        var result = await signInManager.CheckPasswordSignInAsync(user, req.Password, lockoutOnFailure: true);
        if (result.IsLockedOut)
        {
            var lockoutDuration = user.LockoutEnd!.Value - DateTimeOffset.Now;
            var minutes = (int)lockoutDuration.TotalMinutes;
            var seconds = lockoutDuration.Seconds;
            AddError($"User locked out for {minutes}m {seconds}s");
            await SendErrorsAsync(403, ct);
            return;
        }

        if (result.IsNotAllowed)
        {
            if (!user.EmailConfirmed)
            {
                AddError("Email not confirmed.");
                await SendErrorsAsync(500, ct);
                return;
            }

            await userManager.AccessFailedAsync(user);
            AddError("Too many failed login attempts.");
            await SendErrorsAsync(500, ct);
            return;
        }

        if (result is { Succeeded: false, RequiresTwoFactor: false })
        {
            AddError(result.ToString());
            await SendErrorsAsync(500, ct);
            return;
        }

        await jwtService.GenerateJwtAndSetAuthCookie(req.StayLoggedIn, AuthMethodEnum.Password, user, userManager, HttpContext);

        var response = new LoginResponse
        {
            RequiresTwoFactor = result.RequiresTwoFactor,
            Email = user.Email!
        };

        await SendOkAsync(response, ct);
    }
}