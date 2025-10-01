using AdhdTimeOrganizer.application.dto.request.user;
using AdhdTimeOrganizer.application.dto.response.user;
using AdhdTimeOrganizer.domain.extServiceContract.user.auth;
using AdhdTimeOrganizer.domain.model.entity.user;
using FastEndpoints;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using AdhdTimeOrganizer.application.mapper;
using AdhdTimeOrganizer.config;
using AdhdTimeOrganizer.domain.extServiceContract.user;
using AdhdTimeOrganizer.domain.helper;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.infrastructure.persistence.seeder;

namespace AdhdTimeOrganizer.application.endpoint.user.command.auth;

public class RegisterUserEndpoint(
    UserManager<User> userManager,
    IGoogleRecaptchaService googleRecaptchaService,
    ITwoFactorAuthService twoFactorAuthService,
    IUserEmailSenderService emailSender,
    IUserDefaultsService userDefaultsService,
    UserMapper mapper,
    AppCommandDbContext dbContext
) : Endpoint<PasswordRegistrationRequest, TwoFactorAuthResponse>
{
    public override void Configure()
    {
        Post("user/register");
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
            await SendErrorsAsync(403, ct);
            return;
        }

        var existing = await userManager.FindByEmailAsync(req.Email);
        if (existing is not null)
        {
            AddError("User already exists with this email.");
            await SendErrorsAsync(409, ct);
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
            await SendErrorsAsync(duplicate ? 409 : 400, ct);
            return;
        }
        identityResult = await userManager.AddToRoleAsync(newUser, "User");
        if (!identityResult.Succeeded)
        {
            AddError("Failed to add user to role: " + string.Join(", ", identityResult.Errors.Select(e => e.Description)));
            await SendErrorsAsync(500, ct);
            return;
        }
        try
        {
            await SendConfirmationEmail(newUser);
            var twoFaResult = await twoFactorAuthService.SetUpTwoFactorAuth(newUser);
            if (twoFaResult.Failed)
            {
                AddError(twoFaResult.ErrorMessage!);
                await SendErrorsAsync(500, ct);
                return;
            }

            var setDefaultsResult = await userDefaultsService.CreateDefaultsAsync(newUser.Id, ct);
            if (setDefaultsResult.Failed)
            {
                AddError(setDefaultsResult.ErrorMessage!);
                await SendErrorsAsync(500, ct);
                return;
            }

            await tx.CommitAsync(ct);
            await SendOkAsync(twoFaResult.Data, ct);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            AddError(ex.Message);
            await SendErrorsAsync(500, ct);
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