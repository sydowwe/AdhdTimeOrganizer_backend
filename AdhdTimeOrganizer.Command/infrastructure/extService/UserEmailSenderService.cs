using AdhdTimeOrganizer.Command.domain.model.entity.user;
using AdhdTimeOrganizer.Common.infrastructure.extService;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;

namespace AdhdTimeOrganizer.Command.infrastructure.extService;

public class UserEmailSenderService(IConfiguration configuration) : EmailSenderService(configuration), IUserEmailSenderService, IEmailSender<UserEntity>
{
    private readonly string _templatePath = Path.Combine(Directory.GetCurrentDirectory(), "templates", "email");

    public async Task SendConfirmationLinkAsync(UserEntity user, string email, string confirmationLink)
    {
        var template = await File.ReadAllTextAsync(Path.Combine(_templatePath, "ConfirmEmail.html"));
        var htmlContent = template
            .Replace("{{AppName}}", appName)
            .Replace("{{AppLogoUrl}}", appLogo)
            .Replace("{{Email}}", user.Email)
            .Replace("{{ConfirmationLink}}", confirmationLink)
            .Replace("{{CurrentYear}}", DateTime.Now.Year.ToString());

        await SendEmailAsync(email, $"Confirm your email for {appName}", htmlContent);
    }


    public async Task SendPasswordResetLinkAsync(UserEntity user, string email, string resetLink)
    {
        var template = await File.ReadAllTextAsync(Path.Combine(_templatePath, "ResetPassword.html"));
        var htmlContent = template
            .Replace("{{AppName}}", appName)
            .Replace("{{AppLogoUrl}}", appLogo)
            .Replace("{{Email}}", user.Email)
            .Replace("{{ResetLink}}", resetLink)
            .Replace("{{CurrentYear}}", DateTime.Now.Year.ToString());

        await SendEmailAsync(email, $"Reset your {appName} password", htmlContent);
    }

    public async Task SendPasswordResetCodeAsync(UserEntity user, string email, string resetCode)
    {
        var template = await File.ReadAllTextAsync(Path.Combine(_templatePath, "ResetPasswordCode.html"));
        var htmlContent = template
            .Replace("{{AppName}}", appName)
            .Replace("{{AppLogoUrl}}", appLogo)
            .Replace("{{Email}}", user.Email)
            .Replace("{{ResetCode}}", resetCode)
            .Replace("{{CurrentYear}}", DateTime.Now.Year.ToString());

        await SendEmailAsync(email, $"Your {appName} password reset code", htmlContent);
    }
}