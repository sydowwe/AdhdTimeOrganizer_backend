using AdhdTimeOrganizer.application.dto.request.user;
using AdhdTimeOrganizer.application.dto.response.user;
using AdhdTimeOrganizer.domain.extServiceContract.user.auth;
using AdhdTimeOrganizer.domain.model.entity.user;
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
            AddError("Recaptcha verification failed.");
            await SendErrorsAsync(400, ct);
            return;
        }

        var user = await userManager.FindByEmailAsync(req.Email);
        if (user is null)
        {
            // Generic error to prevent user enumeration
            AddError("Invalid email or password");
            await SendErrorsAsync(401, ct);
            return;
        }

        // Check email confirmation first
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
            var response = new LoginResponse
            {
                RequiresTwoFactor = true,
                Email = user.Email!
            };
            await SendOkAsync(response, ct);
            return;
        }

        if (!result.Succeeded)
        {
            // Generic error to prevent user enumeration
            AddError("Invalid email or password");
            await SendErrorsAsync(401, ct);
            return;
        }

        // Successful login - generate JWT and set cookie
        await jwtService.GenerateJwtAndSetAuthCookie(req.StayLoggedIn, AuthMethodEnum.Password, user, userManager, HttpContext);

        var successResponse = new LoginResponse
        {
            RequiresTwoFactor = false,
            Email = user.Email!
        };

        await SendOkAsync(successResponse, ct);
    }
}