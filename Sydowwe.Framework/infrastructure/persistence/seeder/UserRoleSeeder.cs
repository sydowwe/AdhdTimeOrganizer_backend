using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.domain.entity.user;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.infrastructure.persistence.seeder.@interface;

namespace Sydowwe.Framework.infrastructure.persistence.seeder;

/// <summary>
/// Seeds the three <see cref="UserRoleEnum"/> roles. App-wide default rather than a fixture, so it
/// updates in place instead of truncating: <c>user_role</c> is the parent of every user↔role
/// assignment, and wiping it cascades those away — including the root admin's.
/// </summary>
public class UserRoleSeeder(RoleManager<UserRole> roleManager, ILogger<UserRoleSeeder> logger)
    : IScopedService, IAppWideDefaultSeeder
{
    public string SeederName => "UserRole";
    public int Order => 4;

    public async Task Seed(bool overrideData = false)
    {
        List<UserRole> roles =
        [
            new()
            {
                Name = nameof(UserRoleEnum.User),
                Description = "User role",
                IsDefault = true,
                RoleLevel = 1,
                IsAssignable = true
            },
            new()
            {
                Name = nameof(UserRoleEnum.Admin),
                Description = "Local admin role",
                IsDefault = false,
                RoleLevel = 3,
                IsAssignable = false
            },
            new()
            {
                Name = nameof(UserRoleEnum.Root),
                Description = "App administrator role",
                IsDefault = false,
                RoleLevel = 4,
                IsAssignable = false
            }
        ];

        foreach (var role in roles)
            try
            {
                var existingRole = await roleManager.FindByNameAsync(role.Name!);

                if (existingRole != null)
                {
                    if (!overrideData)
                    {
                        logger.LogInformation("Role '{RoleName}' already exists, skipping.", role.Name);
                        continue;
                    }

                    existingRole.Description = role.Description;
                    existingRole.IsDefault = role.IsDefault;
                    existingRole.RoleLevel = role.RoleLevel;
                    existingRole.IsAssignable = role.IsAssignable;

                    await roleManager.UpdateAsync(existingRole);
                    logger.LogInformation("Role '{RoleName}' updated.", role.Name);
                    continue;
                }

                var result = await roleManager.CreateAsync(role);
                if (result.Succeeded)
                {
                    await roleManager.AddClaimAsync(role, new Claim(ClaimTypes.Role, role.Name!));
                    logger.LogInformation("Role '{RoleName}' created.", role.Name);
                }
                else
                {
                    logger.LogError("Failed to create role '{RoleName}': {Errors}", role.Name, result.ToString());
                }
            }
            catch (Exception e)
            {
                logger.LogError(new EventId(1000), e, "Failed to seed user role");
            }
    }
}
