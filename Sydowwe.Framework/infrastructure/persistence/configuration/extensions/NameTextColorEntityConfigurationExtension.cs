using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sydowwe.Framework.domain.entityInterface;

namespace Sydowwe.Framework.infrastructure.persistence.configuration.extensions;

public static class NameTextColorEntityConfigurationExtension
{
    public static void BaseTextColorIconEntityConfigure<TEntity>(this EntityTypeBuilder<TEntity> builder) where TEntity : class, IBaseTextColorIconEntity
    {
        builder.BaseEntityConfigure();
        builder.Property(r => r.Icon).HasMaxLength(50);
    }


    public static void BaseNameTextColorIconEntityConfigure<TEntity>(this EntityTypeBuilder<TEntity> builder) where TEntity : class, IBaseNameTextColorIconEntity
    {
        builder.BaseNameTextColorEntityConfigure();
        builder.Property(r => r.Icon).HasMaxLength(50);
    }

    public static void BaseNameTextEntityConfigure<TEntity>(this EntityTypeBuilder<TEntity> builder) where TEntity : class, IBaseNameTextEntity
    {
        builder.BaseEntityConfigure();
        builder.Property(r => r.Name).HasMaxLength(100).IsUnicode().IsRequired();
        builder.Property(r => r.Text).HasMaxLength(1000).IsUnicode();
    }

    public static void BaseTextColorEntityConfigure<TEntity>(this EntityTypeBuilder<TEntity> builder) where TEntity : class, IBaseTextColorEntity
    {
        builder.BaseEntityConfigure();
        builder.Property(r => r.Text).HasMaxLength(100).IsUnicode().IsRequired();
        builder.Property(r => r.Color).HasMaxLength(7).IsRequired();
    }

    public static void BaseNameTextColorEntityConfigure<TEntity>(this EntityTypeBuilder<TEntity> builder) where TEntity : class, IBaseNameTextColorEntity
    {
        builder.BaseNameTextEntityConfigure();
        builder.Property(r => r.Color).HasMaxLength(7).IsRequired();
    }
}