using System.Security.Claims;
using AdhdTimeOrganizer.config.dependencyInjection;
using AdhdTimeOrganizer.domain.model.entity.user;
using AdhdTimeOrganizer.infrastructure.persistence.seeder;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.infrastructure.persistence.seeders.@default;

public class UserRoleSeeder(RoleManager<UserRole> roleManager, AppCommandDbContext dbContext, ILogger<UserRoleSeeder> logger) : IScopedService, IDefaultDatabaseSeeder
{
    public string SeederName => "UserRole";
    public int Order => 4;

    public async Task TruncateTable()
    {
        await dbContext.TruncateTableAsync<UserRole>();
    }

    public async Task Seed()
    {
        if (await dbContext.UserRoles.AnyAsync())
        {
            logger.LogInformation("User roles already exist, skipping seeding.");
            return;
        }

        List<UserRole> roles =
        [
            new()
            {
                Name = "User",
                Description = "User role",
                IsDefault = true,
                RoleLevel = 1,
                IsAssignable = true
            },
            new()
            {
                Name = "Admin",
                Description = "Local admin role",
                IsDefault = false,
                RoleLevel = 3,
                IsAssignable = false
            },
            new()
            {
                Name = "Root",
                Description = "App administrator role",
                IsDefault = false,
                RoleLevel = 4,
                IsAssignable = false
            },
        ];

        foreach (var role in roles)
        {
            try
            {
                var result = await roleManager.CreateAsync(role);
                if (result.Succeeded)
                {
                    await roleManager.AddClaimAsync(role, new Claim(ClaimTypes.Role, role.Name));
                }
            }
            catch (Exception e)
            {
                logger.LogError(new EventId(1000), e, "Failed to seed user role");
            }
        }
    }
}