using System.Linq.Expressions;
using AdhdTimeOrganizer.domain.model.entity.@base;
using AdhdTimeOrganizer.domain.model.entity.user;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.infrastructure.persistence;

public static class EntityWIthUserBuilderExtensions
{
    public static ReferenceCollectionBuilder<User, TEntity> IsManyWithOneUser<TEntity>(this EntityTypeBuilder<TEntity> builder, Expression<Func<User, IEnumerable<TEntity>?>>? navigationProperty = null, DeleteBehavior deleteBehavior = DeleteBehavior.Cascade) where TEntity : class, IEntityWithUser
    {
       return builder.HasOne(r => r.User)
           .WithMany(navigationProperty)
           .HasForeignKey(r => r.UserId).IsRequired()
           .OnDelete(deleteBehavior);
    }
    public static ReferenceReferenceBuilder<TEntity,User> IsOneWithOneUser<TEntity>(this EntityTypeBuilder<TEntity> builder, Expression<Func<User, TEntity?>>? navigationProperty = null, DeleteBehavior deleteBehavior = DeleteBehavior.Cascade) where TEntity : class, IEntityWithUser
    {
        return builder.HasOne(r => r.User)
            .WithOne(navigationProperty)
            .HasForeignKey<TEntity>(r => r.UserId).IsRequired()
            .OnDelete(deleteBehavior);
    }

    public static void BaseNameTextEntityConfigure<TEntity>(this EntityTypeBuilder<TEntity> builder, bool isNameUnique = true) where TEntity : BaseNameTextEntity
    {
        builder.BaseEntityConfigure();
        builder.Property(r => r.Name).HasMaxLength(100).IsUnicode().IsRequired();
        builder.Property(r => r.Text).HasMaxLength(1000).IsUnicode();
        if (isNameUnique)
        {
            builder.HasIndex(r => new { r.UserId, r.Name })
                .IsUnique();
        }
    }
    public static void BaseTextColorEntityConfigure<TEntity>(this EntityTypeBuilder<TEntity> builder, bool isTextUnique = true) where TEntity : BaseTextColorEntity
    {
        builder.BaseEntityConfigure();
        builder.Property(r => r.Text).HasMaxLength(100).IsUnicode().IsRequired();
        builder.Property(r => r.Color).HasMaxLength(7).IsRequired();
        if (isTextUnique)
        {
            builder.HasIndex(r => new { r.UserId, r.Text })
                .IsUnique();
        }
    }
    public static void BaseNameTextColorEntityConfigure<TEntity>(this EntityTypeBuilder<TEntity> builder) where TEntity : BaseNameTextColorEntity
    {
        builder.BaseNameTextEntityConfigure();
        builder.Property(r => r.Color).HasMaxLength(7).IsRequired();
    }


}