using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sydowwe.Framework.domain.entity.user;

namespace Sydowwe.Framework.infrastructure.persistence.configuration.extensions;

public static class EntityWithUserBuilderExtensions
{
    public static ReferenceCollectionBuilder<TUser, TEntity> IsManyWithOneUser<TUser, TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        Expression<Func<TUser, IEnumerable<TEntity>?>>? navigationProperty = null,
        DeleteBehavior deleteBehavior = DeleteBehavior.Cascade)
        where TUser : BaseUser
        where TEntity : BaseEntityWithUser<TUser>
    {
        builder.HasIndex(r => r.UserId);
        return builder.HasOne(r => r.User)
            .WithMany(navigationProperty)
            .HasForeignKey(r => r.UserId).IsRequired()
            .OnDelete(deleteBehavior);
    }

    public static ReferenceReferenceBuilder<TEntity, TUser> IsOneWithOneUser<TUser, TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        Expression<Func<TUser, TEntity?>>? navigationProperty = null,
        DeleteBehavior deleteBehavior = DeleteBehavior.Cascade)
        where TUser : BaseUser
        where TEntity : BaseEntityWithUser<TUser>
    {
        builder.HasIndex(r => r.UserId);
        return builder.HasOne(r => r.User)
            .WithOne(navigationProperty)
            .HasForeignKey<TEntity>(r => r.UserId).IsRequired()
            .OnDelete(deleteBehavior);
    }
}