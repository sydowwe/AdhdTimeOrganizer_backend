using AdhdTimeOrganizer.domain.model.entity.user;
using Sydowwe.Framework.domain.serviceContract;
using Sydowwe.Framework.infrastructure.persistence.seeder.@interface;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.infrastructure.persistence.seeder.@default;

public class DefaultUsersSeeder(UserManager<User> userManager, AppDbContext dbContext, IUserDefaultsService userDefaultsService, ILogger<DefaultUsersSeeder> logger)
    : IScopedService, IAppWideDefaultSeeder
{
    public string SeederName => "User";
    public int Order => 5;

    public async Task Seed(bool overrideData = false)
    {
        var adminUser = new User
        {
            Email = Helper.GetEnvVar("ROOT_ADMIN_EMAIL"),

            EmailConfirmed = true,
            Locale = AvailableLocales.Sk,
            Timezone = TimeZoneInfo.Local,
            HasExtensionAccess = true
        };

        var existingAdmin = await dbContext.Users
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
                    existingAdmin.HasExtensionAccess = adminUser.HasExtensionAccess;

                    var updateResult = await userManager.UpdateAsync(existingAdmin);
                    if (!updateResult.Succeeded)
                        logger.LogError("Failed to update root admin user during override.");

                    var token = await userManager.GeneratePasswordResetTokenAsync(existingAdmin);
                    var resetResult = await userManager.ResetPasswordAsync(existingAdmin, token, Helper.GetEnvVar("ROOT_ADMIN_PASSWORD"));
                    if (!resetResult.Succeeded)
                        logger.LogError("Failed to reset root admin password during override.");
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
                logger.LogError("Failed to create root admin user.");
                return;
            }

            result = await userManager.AddToRoleAsync(adminUser, nameof(UserRoleEnum.Root));
            if (!result.Succeeded)
                logger.LogError("Failed to assign Root role to root admin user.");

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