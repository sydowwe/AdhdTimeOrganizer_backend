using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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

        modelBuilder.Ignore<IdentityUserLogin<long>>();
        modelBuilder.Ignore<IdentityUserClaim<long>>();
        modelBuilder.Entity<IdentityUserToken<long>>(entity => entity.ToTable("user_token"));
        modelBuilder.Entity<IdentityUserRole<long>>(entity => entity.ToTable("user__role"));
        modelBuilder.Entity<IdentityRoleClaim<long>>(entity => entity.ToTable("user_role_claim"));

        // Audit log configurations live in the Framework assembly.
        // User entity configuration lives in the Core assembly (see AppCoreDbContext).
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AuditLogEntityConfiguration).Assembly);

        // RefreshToken always belongs to a user. The FK is configured here (not in the
        // RefreshToken entity configuration) because only here is the concrete user type
        // known — TUser — since the abstract BaseUser is excluded from the model above.
        modelBuilder.Entity<RefreshToken>()
            .HasOne<TUser>()
            .WithMany()
            .HasForeignKey(rt => rt.UserId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        this.BaseSaveChangesAsync();
        this.BaseWithUserEntitySaveChangesAsync(loggedUserService, logger);
        return await base.SaveChangesAsync(cancellationToken);
    }
}