using AdhdTimeOrganizer.config.dependencyInjection;
using AdhdTimeOrganizer.domain.extServiceContract.user;
using AdhdTimeOrganizer.domain.helper;
using AdhdTimeOrganizer.domain.model.entity.user;

namespace AdhdTimeOrganizer.infrastructure.extService.user;

public class UserEmailSenderService(IConfiguration configuration) : EmailSenderService(configuration), IUserEmailSenderService, ISingletonService
{
    private readonly string _templatePath = Path.Combine(Directory.GetCurrentDirectory(), "infrastructure", "templates", "email");

    public async Task SendConfirmationLinkAsync(User user, string email, string token)
    {
        var confirmationLink =
            $"{Helper.GetEnvVar("PAGE_URL")}/confirm-email?userId={user.Id}&token={Uri.EscapeDataString(token)}";
        var template = await File.ReadAllTextAsync(Path.Combine(_templatePath, "ConfirmEmail.html"));
        var htmlContent = template
            .Replace("{{AppName}}", appName)
            .Replace("{{AppLogoUrl}}", appLogo)
            .Replace("{{Email}}", user.Email)
            .Replace("{{ConfirmationLink}}", confirmationLink)
            .Replace("{{CurrentYear}}", DateTime.Now.Year.ToString());

        await SendEmailAsync(email, $"Confirm your email for {appName}", htmlContent);
    }

    public async Task SendPasswordResetLinkAsync(User user, string email, string resetLink)
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

    public async Task SendPasswordResetCodeAsync(User user, string email, string resetCode)
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