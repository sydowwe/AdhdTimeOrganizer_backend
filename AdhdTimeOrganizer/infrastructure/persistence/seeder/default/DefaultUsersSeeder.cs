using AdhdTimeOrganizer.config.dependencyInjection;
using AdhdTimeOrganizer.domain.helper;
using AdhdTimeOrganizer.domain.model.entity.user;
using AdhdTimeOrganizer.domain.model.@enum;
using AdhdTimeOrganizer.infrastructure.persistence.seeder;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.infrastructure.persistence.seeders.@default;

public class DefaultUsersSeeder(UserManager<User> userManager, AppCommandDbContext dbContext, ILogger<DefaultUsersSeeder> logger) : IScopedService, IDefaultDatabaseSeeder
{
    public string SeederName => "User";
    public int Order => 5;

    public async Task TruncateTable()
    {
        await dbContext.TruncateTableAsync<User>();
    }

    public async Task Seed()
    {
        var adminUser = new User
        {
            UserName = Helper.GetEnvVar("ROOT_ADMIN_USERNAME"),
            Email = Helper.GetEnvVar("ROOT_ADMIN_EMAIL"),

            EmailConfirmed = true,
            CurrentLocale = AvailableLocales.Sk,
            Timezone = TimeZoneInfo.Local,
        };

        if (await dbContext.Users.AnyAsync(u => u.UserName == adminUser.UserName))
        {
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
        }
        catch (Exception e)
        {
            logger.LogError(new EventId(1000), e, "Failed to seed user database");
        }
    }
}