using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sydowwe.Framework.domain.entity.user;
using Sydowwe.Framework.domain.extServiceContract.user;

namespace Sydowwe.Framework.infrastructure.persistence;

public static class UserDbContextExtensions
{
    public static void BaseWithUserEntitySaveChangesAsync(this DbContext dbContext, ILoggedUserService loggedUserService, ILogger logger)
    {
        if (dbContext.ChangeTracker.Entries<IEntityWithUser>().Any(entry => entry.State == EntityState.Added))
        {
            if (!loggedUserService.IsAuthenticated)
                return;
            try
            {
                var userId = loggedUserService.GetUserId;
                foreach (var entry in dbContext.ChangeTracker.Entries<IEntityWithUser>())
                    // Only FILL IN an unset owner, never overwrite one the caller set deliberately.
                    // Some rows are written on behalf of a user who is not the caller — a refresh
                    // token minted during login is the clearest case: RefreshTokenService knows whose
                    // it is, and stamping the ambient user here silently reassigns the session to the
                    // wrong account.
                    if (entry.State == EntityState.Added && entry.Entity.UserId == 0)
                        entry.Entity.UserId = userId;
            }
            catch (Exception ex)
            {
                logger.LogError("Failed to get logged user ID: {message}", ex.Message);
            }
        }
    }

    /// <summary>
    /// Applies a global query filter to every entity implementing <c>IEntityWithUser</c> so that
    /// queries automatically scope to the current user.
    ///
    /// <para><b>Opt-in and off by default</b> — it does nothing unless
    /// <see cref="UserScopingOptions.Enabled"/> is set for this deployment. Read
    /// <see cref="UserScopingOptions"/> before turning it on: the filter is wrong in a deployment
    /// where an admin or HR tier reads across users, and it fails <i>silently</i> (empty results,
    /// not 403) when it is.</para>
    ///
    /// <para>Exclusions come from two places, both applied: <paramref name="excludeTypes"/> for
    /// exclusions that belong to the code, and <see cref="UserScopingOptions.ExcludedEntities"/> for
    /// ones that belong to the deployment.</para>
    /// </summary>
    /// <returns>
    /// The names of the entities that were filtered, in model order — empty when the feature is off.
    /// Returned rather than logged here so the caller decides where it goes; a global filter that
    /// nothing announces is the hardest kind of scoping bug to track down later.
    /// </returns>
    public static IReadOnlyList<string> ApplyUserQueryFilters(
        this ModelBuilder modelBuilder,
        ILoggedUserService? loggedUserService,
        UserScopingOptions? options,
        IEnumerable<Type>? excludeTypes = null)
    {
        if (loggedUserService is null || options is not { Enabled: true })
            return [];

        var excludedTypes = excludeTypes?.ToHashSet() ?? [];
        var excludedNames = options.ExcludedEntities.ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Captured as a constant in the model, which EF caches per context type — so this is the
        // instance from whichever context built the model first. Safe only because
        // LoggedUserService resolves through IHttpContextAccessor on every property read rather
        // than snapshotting the principal at construction. Do not replace it with an implementation
        // that caches the user.
        var serviceConstant = Expression.Constant(loggedUserService, typeof(ILoggedUserService));
        var isAuthenticated = Expression.Property(serviceConstant, nameof(ILoggedUserService.IsAuthenticated));
        var getUserId = Expression.Property(serviceConstant, nameof(ILoggedUserService.GetUserId));

        var applied = new List<string>();

        foreach (var entityType in modelBuilder.Model.GetEntityTypes()
                     .Where(t => typeof(IEntityWithUser).IsAssignableFrom(t.ClrType)
                                 && !excludedTypes.Contains(t.ClrType)
                                 && !excludedNames.Contains(t.ClrType.Name)))
        {
            var param = Expression.Parameter(entityType.ClrType, "e");
            var userIdProp = Expression.Property(param, nameof(IEntityWithUser.UserId));
            // !isAuthenticated || e.UserId == currentUserId
            // OrElse short-circuits, so GetUserId — which throws when unauthenticated — is never
            // evaluated for an anonymous caller.
            var body = Expression.OrElse(
                Expression.Not(isAuthenticated),
                Expression.Equal(userIdProp, getUserId));
            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(Expression.Lambda(body, param));
            applied.Add(entityType.ClrType.Name);
        }

        return applied;
    }
}