using AdhdTimeOrganizer.domain.model.entity.user;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.Contracts.user;
using Sydowwe.Framework.domain.helper;
using Sydowwe.Framework.infrastructure.persistence.seeder;

namespace AdhdTimeOrganizer.infrastructure.persistence.seeder;

/// <summary>
/// Host-side implementation of the two seams seeding code uses to find users: <c>Sydowwe.Framework.Contracts</c>'s
/// <see cref="ISeedUserIdProvider"/> (module dev seeders) and the framework's
/// <see cref="ISeedUserProvider"/> (the per-user seeder managers). Neither can query
/// <see cref="User"/> itself — they key everything by a plain UserId and do not reference this
/// project — so the composition root resolves ids on their behalf.
/// </summary>
public class SeedUserIdProvider(AppDbContext dbContext) : ISeedUserIdProvider, ISeedUserProvider, IScopedService
{
    public async Task<IReadOnlyList<long>> GetSeedUserIdsAsync(int count, CancellationToken ct = default)
    {
        return await dbContext.Users
            .AsNoTracking()
            .OrderBy(u => u.Id)
            .Select(u => u.Id)
            .Take(count)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<long>> GetAllUserIdsAsync(CancellationToken ct = default)
    {
        return await dbContext.Users
            .AsNoTracking()
            .OrderBy(u => u.Id)
            .Select(u => u.Id)
            .ToListAsync(ct);
    }

    public async Task<long?> GetRootAdminUserIdAsync(CancellationToken ct = default)
    {
        var rootAdminEmail = Helper.GetEnvVar("ROOT_ADMIN_EMAIL");

        // Matched on UserName, not Email, because that is the column DefaultUsersSeeder writes and
        // looks the admin up by.
        return await dbContext.Users
            .AsNoTracking()
            .Where(u => u.UserName == rootAdminEmail)
            .Select(u => (long?)u.Id)
            .FirstOrDefaultAsync(ct);
    }
}