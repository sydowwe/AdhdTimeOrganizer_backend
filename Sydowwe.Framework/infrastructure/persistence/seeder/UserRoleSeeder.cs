using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.domain.auth;
using Sydowwe.Framework.domain.entity.user;
using Sydowwe.Framework.infrastructure.persistence.seeder.@interface;

namespace Sydowwe.Framework.infrastructure.persistence.seeder;

/// <summary>
/// Seeds the roles declared by the deployment's <see cref="IRoleCatalog"/> — the framework no longer
/// knows any role names of its own, so a host's catalog is the single source of truth for both the
/// rows seeded here and the gates enforced by <c>EndpointExtensions</c>. They cannot drift.
///
/// <para>App-wide default rather than a fixture, so it updates in place instead of truncating:
/// <c>user_role</c> is the parent of every user-to-role assignment, and wiping it cascades those
/// away — including the root admin's.</para>
///
/// <para><b>Renaming a role is not a code change.</b> Role names are persisted on <c>user_role</c>,
/// on the assignment table, and (in this solution) on business columns such as <c>job_title.role</c>.
/// This seeder matches existing rows <i>by name</i>, so a renamed role is seeded as a NEW row and
/// every account keeps the old one — silently losing all access. Rename via a data migration first.</para>
/// </summary>
public class UserRoleSeeder(RoleManager<UserRole> roleManager, ILogger<UserRoleSeeder> logger)
    : IScopedService, IAppWideDefaultSeeder
{
    public string SeederName => "UserRole";
    public int Order => 4;

    public async Task Seed(bool overrideData = false)
    {
        var roles = FrameworkRoles.Catalog.All
            .Select(definition => new UserRole
            {
                Name = definition.Name,
                Description = definition.Description,
                IsDefault = definition.IsDefault,
                // The tier IS the level - keeping a separately hand-numbered RoleLevel is how the
                // two silently disagree after someone adds a role in only one of the two places.
                RoleLevel = (int)definition.Tier,
                IsAssignable = definition.IsAssignable
            })
            .ToList();

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