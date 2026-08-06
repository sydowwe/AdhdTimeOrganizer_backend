using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sydowwe.Framework.domain.audit;
using Sydowwe.Framework.domain.entity.user;
using Sydowwe.Framework.domain.extServiceContract.user;
using Sydowwe.Framework.infrastructure.persistence;
using Sydowwe.Framework.infrastructure.persistence.configuration;

namespace Sydowwe.Framework.infrastructure;

public abstract partial class BaseDbContext<TUser>(DbContextOptions options, ILoggedUserService loggedUserService, ILogger logger)
    : IdentityDbContext<TUser, UserRole, long>(options)
    where TUser : BaseUser
{
    public DbSet<RefreshToken> RefreshTokens { get; set; }

    public DbSet<AuditLog> AuditLogs { get; set; }
    public DbSet<BusinessAuditLog> BusinessAuditLogs { get; set; }


    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("public");

        base.OnModelCreating(modelBuilder);

        // Exclude the abstract User base from the EF model — prevents EF from setting up
        // TPH with BaseUser as root and the concrete user as a discriminator-based derived type.
        // The concrete TUser is the only mapped entity for the user table.
        modelBuilder.Ignore<BaseUser>();

        ConfigureIdentityModel(modelBuilder);

        ApplyFrameworkConfigurations(modelBuilder);

        ConfigureRefreshTokenUserFk(modelBuilder);

        // Host configurations run before scoping on purpose: the filter loop walks the entity types
        // already in the model, so an entity that only enters it through a host configuration (or a
        // navigation one of them discovers) would otherwise never be filtered.
        ApplyHostConfigurations(modelBuilder);

        ApplyUserScopingIfEnabled(modelBuilder);

        OnModelCreatingPartial(modelBuilder);
    }

    /// <summary>
    /// Maps the Identity satellite tables. Override to change the mapping — a host whose database
    /// already has <c>user_claim</c> / <c>user_login</c> tables maps them instead of ignoring them.
    /// </summary>
    protected virtual void ConfigureIdentityModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Ignore<IdentityUserLogin<long>>();
        modelBuilder.Ignore<IdentityUserClaim<long>>();
        modelBuilder.Entity<IdentityUserToken<long>>(entity => entity.ToTable("user_token"));
        modelBuilder.Entity<IdentityUserRole<long>>(entity => entity.ToTable("user__role"));
        modelBuilder.Entity<IdentityRoleClaim<long>>(entity => entity.ToTable("user_role_claim"));
    }

    /// <summary>
    /// Applies the Framework assembly's own entity configurations (audit logs). Override in a host
    /// that deliberately does not map all of them — see <c>AppDbContext</c>, which maps
    /// <c>business_audit_log</c> but not the partitioned <c>audit_log</c>.
    /// <para>User entity configuration lives in the host assembly, not here.</para>
    /// </summary>
    protected virtual void ApplyFrameworkConfigurations(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsFromAssembly(typeof(AuditLogEntityConfiguration).Assembly);

    /// <summary>
    /// RefreshToken always belongs to a user. The FK is configured here (not in the RefreshToken
    /// entity configuration) because only here is the concrete user type known — TUser — since the
    /// abstract BaseUser is excluded from the model above. Override (to a no-op) in a host that
    /// configures the relationship itself, e.g. to give the principal end a navigation.
    /// </summary>
    protected virtual void ConfigureRefreshTokenUserFk(ModelBuilder modelBuilder)
        => modelBuilder.Entity<RefreshToken>()
            .HasOne<TUser>()
            .WithMany()
            .HasForeignKey(rt => rt.UserId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

    /// <summary>
    /// The host's own entity configurations. Runs after the Framework's and before user scoping.
    /// </summary>
    protected virtual void ApplyHostConfigurations(ModelBuilder modelBuilder)
    {
    }

    /// <summary>
    /// Applies the optional per-user global query filter, gated on <see cref="UserScopingOptions"/>.
    /// Off unless a deployment binds <c>UserScoping:Enabled = true</c>, so this is a no-op for hosts
    /// that never opt in.
    ///
    /// <para>Options are pulled from the application service provider rather than a constructor
    /// parameter so that no host has to change its context signature to gain the switch. A
    /// design-time factory has no application provider, so migrations are always generated with
    /// scoping off — which is correct: query filters are a query-time concern and never appear in
    /// the migration model, so there is no snapshot drift either way.</para>
    ///
    /// <para>Override <see cref="UserScopingExcludedTypes"/> to exempt entities in code.</para>
    /// </summary>
    private void ApplyUserScopingIfEnabled(ModelBuilder modelBuilder)
    {
        var appServices = options.FindExtension<CoreOptionsExtension>()?.ApplicationServiceProvider;
        var scopingOptions = appServices?.GetService<IOptions<UserScopingOptions>>()?.Value;

        var filtered = modelBuilder.ApplyUserQueryFilters(loggedUserService, scopingOptions, UserScopingExcludedTypes);

        // Logged once per model build. A global filter that silently narrows every read is the
        // hardest scoping bug to diagnose from the outside — the startup log is where someone
        // debugging "the admin grid is empty" will actually look.
        if (filtered.Count > 0)
            logger.LogWarning("Per-user query scoping is ENABLED for {Count} entities: {Entities}", filtered.Count, string.Join(", ", filtered));
    }

    /// <summary>
    /// Entities exempt from per-user query scoping in code (as opposed to
    /// <see cref="UserScopingOptions.ExcludedEntities"/>, which exempts them per deployment).
    /// Override in a concrete context for entities that are user-owned as data but are legitimately
    /// read across users by the application itself.
    /// </summary>
    protected virtual IEnumerable<Type>? UserScopingExcludedTypes => null;

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        this.BaseSaveChangesAsync();
        this.BaseWithUserEntitySaveChangesAsync(loggedUserService, logger);
        return await base.SaveChangesAsync(cancellationToken);
    }
}