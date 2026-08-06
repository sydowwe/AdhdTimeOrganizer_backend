using Microsoft.Extensions.Configuration;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.domain.entity.user;
using Sydowwe.Framework.domain.extServiceContract.user;
using Sydowwe.Framework.domain.helper;

namespace Sydowwe.Framework.infrastructure.extService.user;

public class UserEmailSenderService<TUser>(IConfiguration configuration) : EmailSenderService(configuration), IUserEmailSenderService<TUser>, ISingletonService
    where TUser : BaseUser
{
    /// <summary>
    /// Optional per-host template overrides. A file here shadows the embedded template of the same
    /// name; anything absent falls back to the one shipped in this assembly, so a host overrides one
    /// template without having to re-supply the rest.
    /// </summary>
    private readonly string _overridePath = Path.Combine(AppContext.BaseDirectory, "templates", "email");

    /// <summary>
    /// Reads an email template by file name.
    ///
    /// <para>The embedded copy is the source of truth. This used to read
    /// <c>Directory.GetCurrentDirectory()/templates/email</c> off disk, which resolved to a path that
    /// never existed in any environment — the templates lived under <c>infrastructure/templates/email</c>
    /// and the host's copy rule was a no-op — so every mail this class sends threw
    /// <see cref="FileNotFoundException"/>. Embedding removes both failure modes: nothing to copy, and
    /// no working-directory assumption.</para>
    /// </summary>
    private async Task<string> ReadTemplateAsync(string fileName)
    {
        var overrideFile = Path.Combine(_overridePath, fileName);
        if (File.Exists(overrideFile))
            return await File.ReadAllTextAsync(overrideFile);

        var assembly = typeof(UserEmailSenderService<>).Assembly;
        // Resource names are <RootNamespace>.<dir>.<dir>.<file>, and RootNamespace defaults to the
        // assembly name for this project.
        var resourceName = $"{assembly.GetName().Name}.infrastructure.templates.email.{fileName}";

        await using var stream = assembly.GetManifestResourceStream(resourceName)
                                 ?? throw new InvalidOperationException(
                                     $"Email template '{fileName}' is missing. Expected embedded resource '{resourceName}' " +
                                     $"in {assembly.GetName().Name}, or an override at '{overrideFile}'.");

        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    public async Task SendConfirmationLinkAsync(TUser user, string token)
    {
        var confirmationLink =
            $"{Helper.GetEnvVar("PAGE_URL")}/confirm-email?userId={user.Id}&token={Uri.EscapeDataString(token)}";
        var template = await ReadTemplateAsync("ConfirmEmail.html");
        var htmlContent = template
            .Replace("{{AppName}}", appName)
            .Replace("{{AppLogoUrl}}", appLogo)
            .Replace("{{Email}}", user.Email)
            .Replace("{{ConfirmationLink}}", confirmationLink)
            .Replace("{{CurrentYear}}", DateTime.UtcNow.Year.ToString());

        await SendEmailAsync(user.Email!, $"Confirm your email for {appName}", htmlContent);
    }

    public async Task SendEmailChangeConfirmationAsync(TUser user, string newEmail, string token)
    {
        var confirmationLink =
            $"{Helper.GetEnvVar("PAGE_URL")}/confirm-email-change?userId={user.Id}&email={Uri.EscapeDataString(newEmail)}&token={Uri.EscapeDataString(token)}";
        var template = await ReadTemplateAsync("ConfirmEmail.html");
        var htmlContent = template
            .Replace("{{AppName}}", appName)
            .Replace("{{AppLogoUrl}}", appLogo)
            .Replace("{{Email}}", user.Email)
            .Replace("{{ConfirmationLink}}", confirmationLink)
            .Replace("{{CurrentYear}}", DateTime.UtcNow.Year.ToString());

        await SendEmailAsync(user.Email!, $"Confirm your email for {appName}", htmlContent);
    }

    public async Task SendPasswordResetLinkAsync(TUser user, string email, string resetLink)
    {
        var template = await ReadTemplateAsync("ResetPassword.html");
        var htmlContent = template
            .Replace("{{AppName}}", appName)
            .Replace("{{AppLogoUrl}}", appLogo)
            .Replace("{{Email}}", user.Email)
            .Replace("{{ResetLink}}", resetLink)
            .Replace("{{CurrentYear}}", DateTime.UtcNow.Year.ToString());

        await SendEmailAsync(email, $"Reset your {appName} password", htmlContent);
    }

    public async Task SendPasswordResetCodeAsync(TUser user, string resetCode)
    {
        var template = await ReadTemplateAsync("ResetPasswordCode.html");
        var htmlContent = template
            .Replace("{{AppName}}", appName)
            .Replace("{{AppLogoUrl}}", appLogo)
            .Replace("{{Email}}", user.Email)
            .Replace("{{ResetCode}}", resetCode)
            .Replace("{{CurrentYear}}", DateTime.UtcNow.Year.ToString());

        await SendEmailAsync(user.Email!, $"Your {appName} password reset code", htmlContent);
    }
}