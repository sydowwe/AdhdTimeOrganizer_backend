using AdhdTimeOrganizer.config.dependencyInjection;
using AdhdTimeOrganizer.domain.extServiceContract.user;
using AdhdTimeOrganizer.domain.model.entity.user;

namespace AdhdTimeOrganizer.infrastructure.extService.user;

public class UserEmailSenderService(IConfiguration configuration) : EmailSenderService(configuration), IUserEmailSenderService, ISingletonService
{
    private readonly string _templatePath = Path.Combine(Directory.GetCurrentDirectory(), "templates", "email");

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
}