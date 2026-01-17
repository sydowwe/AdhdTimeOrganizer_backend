using AdhdTimeOrganizer.config.dependencyInjection;
using AdhdTimeOrganizer.domain.helper;
using AdhdTimeOrganizer.domain.model.entity.user;
using AdhdTimeOrganizer.domain.model.@enum;
using AdhdTimeOrganizer.infrastructure.persistence.seeders;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.infrastructure.persistence.seeder.@default;

public class DefaultUsersSeeder(UserManager<User> userManager, AppCommandDbContext dbContext, IUserDefaultsService userDefaultsService, ILogger<DefaultUsersSeeder> logger) : IScopedService, IDefaultDatabaseSeeder
{
    public string SeederName => "User";
    public int Order => 5;

    public async Task Seed(bool overrideData = false)
    {
        var adminUser = new User
        {
            UserName = Helper.GetEnvVar("ROOT_ADMIN_USERNAME"),
            Email = Helper.GetEnvVar("ROOT_ADMIN_EMAIL"),

            EmailConfirmed = true,
            CurrentLocale = AvailableLocales.Sk,
            Timezone = TimeZoneInfo.Local,
        };

        var existingAdmin = await dbContext.Users
            .FirstOrDefaultAsync(u => u.UserName == adminUser.UserName);
        if (existingAdmin != null)
        {
            if (overrideData)
            {
                existingAdmin.Email = adminUser.Email;
                existingAdmin.CurrentLocale = adminUser.CurrentLocale;
                existingAdmin.Timezone = adminUser.Timezone;
                existingAdmin.EmailConfirmed = true;

                await userManager.UpdateAsync(existingAdmin);

                // If you also need to reset the password during override:
                var token = await userManager.GeneratePasswordResetTokenAsync(existingAdmin);
                await userManager.ResetPasswordAsync(existingAdmin, token, Helper.GetEnvVar("ROOT_ADMIN_PASSWORD"));
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
                logger.LogError(result.ToString());
            }

            result = await userManager.AddToRoleAsync(adminUser, "Root");
            if (!result.Succeeded)
            {
                logger.LogError(result.ToString());
            }

            // Use the handler directly instead of FastEndpoints command execution
            var defaultsRes = await userDefaultsService.CreateDefaultsAsync(adminUser.Id);
            if (defaultsRes.Failed)
            {
                logger.LogError(defaultsRes.ErrorMessage);
            }
        }
        catch (Exception e)
        {
            logger.LogError(new EventId(1000), e, "Failed to seed user database");
        }
    }
}