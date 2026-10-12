using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.Core.infrastructure.security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.domain.helper;
using Sydowwe.Framework.domain.serviceContract;
using Sydowwe.Framework.infrastructure.persistence.seeder.@interface;

namespace AdhdTimeOrganizer.Core.infrastructure.persistence.seeder.@default;

public class DefaultUsersSeeder(UserManager<User> userManager, DbContext dbContext, IUserDefaultsService userDefaultsService, ILogger<DefaultUsersSeeder> logger)
    : IScopedService, IAppWideDefaultSeeder
{
    public string SeederName => "User";
    public int Order => 20;

    /// <summary>
    /// Identity failure codes, joined. Codes only — never <c>Description</c>, which interpolates the
    /// user name and email into the message and would put the root admin's address in the log. The
    /// codes (<c>DuplicateEmail</c>, <c>PasswordTooShort</c>, …) are what actually tells you why.
    /// <c>Seed_DuplicateEmailFailure_NeverLogsTheRawEmailAddress</c> pins that.
    /// </summary>
    private static string Codes(IdentityResult result) => string.Join("; ", result.Errors.Select(e => e.Code));

    /// <summary>
    /// The seeded root admin's time zone. Deliberately not <c>TimeZoneInfo.Local</c>: that would make
    /// the account's dashboards read differently depending on which host happened to run the seeder —
    /// the exact defect <c>IUserTimeZoneResolver</c> refuses to have when it falls back to UTC rather
    /// than the server's zone. Set <c>ROOT_ADMIN_TIMEZONE</c> to an IANA id to override.
    /// </summary>
    private TimeZoneInfo RootAdminTimezone()
    {
        var id = Environment.GetEnvironmentVariable("ROOT_ADMIN_TIMEZONE");
        if (string.IsNullOrWhiteSpace(id))
            return TimeZoneInfo.Utc;

        if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var timezone))
            return timezone;

        logger.LogWarning("ROOT_ADMIN_TIMEZONE is not a time zone id this host knows; seeding the root admin as UTC.");
        return TimeZoneInfo.Utc;
    }

    public async Task Seed(bool overrideData = false)
    {
        var adminUser = new User
        {
            Email = Helper.GetEnvVar("ROOT_ADMIN_EMAIL"),

            EmailConfirmed = true,
            Locale = AvailableLocales.Sk,
            Timezone = RootAdminTimezone()
        };

        var existingAdmin = await dbContext.Set<User>()
            .FirstOrDefaultAsync(u => u.UserName == adminUser.UserName);
        if (existingAdmin != null)
        {
            if (overrideData)
            {
                try
                {
                    existingAdmin.Email = adminUser.Email;
                    existingAdmin.Locale = adminUser.Locale;
                    existingAdmin.Timezone = adminUser.Timezone;
                    existingAdmin.EmailConfirmed = true;

                    var updateResult = await userManager.UpdateAsync(existingAdmin);
                    if (!updateResult.Succeeded)
                        logger.LogError("Failed to update root admin user during override: {IdentityErrorCodes}", Codes(updateResult));

                    var token = await userManager.GeneratePasswordResetTokenAsync(existingAdmin);
                    var resetResult = await userManager.ResetPasswordAsync(existingAdmin, token, Helper.GetEnvVar("ROOT_ADMIN_PASSWORD"));
                    if (!resetResult.Succeeded)
                        logger.LogError("Failed to reset root admin password during override: {IdentityErrorCodes}", Codes(resetResult));
                }
                catch (Exception e)
                {
                    logger.LogError(new EventId(1000), e, "Failed to override root admin user");
                }

                return;
            }

            logger.LogInformation("Root admin user already exists, skipping seeding.");
            return;
        }

        try
        {
            var result = await userManager.CreateAsync(adminUser, Helper.GetEnvVar("ROOT_ADMIN_PASSWORD"));
            if (!result.Succeeded)
            {
                logger.LogError("Failed to create root admin user: {IdentityErrorCodes}", Codes(result));
                return;
            }

            result = await userManager.AddToRoleAsync(adminUser, PortalRoleCatalog.Root);
            if (!result.Succeeded)
                logger.LogError("Failed to assign Root role to root admin user: {IdentityErrorCodes}", Codes(result));

            // Use the handler directly instead of FastEndpoints command execution
            var defaultsRes = await userDefaultsService.CreateDefaultsAsync(adminUser.Id);
            if (defaultsRes.Failed)
                logger.LogError("Failed to create defaults for root admin user.");
        }
        catch (Exception e)
        {
            logger.LogError(new EventId(1000), e, "Failed to seed user database");
        }
    }
}