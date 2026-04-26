using AdhdTimeOrganizer.application.dto.request.user;
using AdhdTimeOrganizer.application.dto.response.user;
using AdhdTimeOrganizer.application.mapper;
using AdhdTimeOrganizer.domain.extServiceContract.user;
using AdhdTimeOrganizer.domain.extServiceContract.user.auth;
using AdhdTimeOrganizer.domain.model.entity.user;
using AdhdTimeOrganizer.domain.serviceContract;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Microsoft.AspNetCore.Identity;

namespace AdhdTimeOrganizer.application.endpoint.user.command.auth.passwordAuth;

public class RegisterUserEndpoint(
    UserManager<User> userManager,
    IGoogleRecaptchaService googleRecaptchaService,
    ITwoFactorAuthService twoFactorAuthService,
    IUserEmailSenderService emailSender,
    IUserDefaultsService userDefaultsService,
    UserMapper mapper,
    AppDbContext dbContext
) : Endpoint<PasswordRegistrationRequest, TwoFactorAuthResponse>
{
    public override void Configure()
    {
        Post("auth/register");
        AllowAnonymous();
        Throttle(hitLimit: 5, durationSeconds: 60, headerName: "X-Client-Id");
        Summary(s => s.Summary = "Register a user with email & password");
    }

    public override async Task HandleAsync(PasswordRegistrationRequest req, CancellationToken ct)
    {
        var recaptchaResult = await googleRecaptchaService.VerifyRecaptchaAsync(req.RecaptchaToken, "register");
        if (recaptchaResult.Failed)
        {
            AddError("Recaptcha failed.");
            await Send.ErrorsAsync(403, ct);
            return;
        }

        var existing = await userManager.FindByEmailAsync(req.Email);
        if (existing is not null)
        {
            AddError("User already exists with this email.");
            await Send.ErrorsAsync(409, ct);
            return;
        }

        var newUser = mapper.ToEntity(req);

        await using var tx = await dbContext.Database.BeginTransactionAsync(ct);
        var identityResult = await userManager.CreateAsync(newUser, req.Password);
        if (!identityResult.Succeeded)
        {
            var duplicate = identityResult.Errors.Any(e => e.Code is "DuplicateUserName" or "DuplicateEmail");
            var msg = duplicate
                ? $"User already exists with EMAIL: {newUser.Email}"
                : "Failed to register user: " + string.Join(", ", identityResult.Errors.Select(e => e.Description));
            AddError(msg);
            await Send.ErrorsAsync(duplicate ? 409 : 400, ct);
            return;
        }
        identityResult = await userManager.AddToRoleAsync(newUser, "User");
        if (!identityResult.Succeeded)
        {
            AddError("Failed to add user to role: " + string.Join(", ", identityResult.Errors.Select(e => e.Description)));
            await Send.ErrorsAsync(500, ct);
            return;
        }

        try
        {
            await SendConfirmationEmail(newUser);
            var twoFaResult = await twoFactorAuthService.SetUpTwoFactorAuth(newUser);
            if (twoFaResult.Failed)
            {
                AddError(twoFaResult.ErrorMessage!);
                await Send.ErrorsAsync(500, ct);
                return;
            }

            var setDefaultsResult = await userDefaultsService.CreateDefaultsAsync(newUser.Id, ct);
            if (setDefaultsResult.Failed)
            {
                AddError(setDefaultsResult.ErrorMessage!);
                await Send.ErrorsAsync(500, ct);
                return;
            }

            await tx.CommitAsync(ct);
            await Send.OkAsync(twoFaResult.Data, ct);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            AddError(ex.Message);
            await Send.ErrorsAsync(500, ct);
        }
    }

    private async Task SendConfirmationEmail(User user)
    {
        if (!user.EmailConfirmed)
        {
            var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
            await emailSender.SendConfirmationLinkAsync(user, token);
        }
    }
}