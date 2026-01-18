using System.Linq.Expressions;
using AdhdTimeOrganizer.domain.model.entity;
using AdhdTimeOrganizer.domain.model.entityInterface;
using Humanizer;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.infrastructure.persistence;

public static class EntityBuilderExtensions
{
    public static void BaseEntityConfigure<TEntity>(this EntityTypeBuilder<TEntity> builder) where TEntity : class, IBaseTableEntity
    {
        var tableName = typeof(TEntity).Name.Underscore();
        builder.ToTable(tableName);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).UseSerialColumn();

        builder.Property<uint>("row_version")
            .IsConcurrencyToken()
            .IsRowVersion();
        builder.Property(x => x.CreatedTimestamp).HasDefaultValueSql("now()")
            .IsRequired();
        builder.Property(x => x.ModifiedTimestamp).HasDefaultValueSql("now()")
            .IsRequired();
    }

    public static PropertyBuilder<TEnum> EnumColumn<TEntity, TEnum>(this EntityTypeBuilder<TEntity> builder, Expression<Func<TEntity, TEnum>> column, bool isRequired = true)
        where TEntity : class, IEntity
        where TEnum : struct, Enum
    {
        return builder.Property(column)
            .HasConversion(
                (TEnum v) => v.ToString(),
                (string? v) => string.IsNullOrEmpty(v) ? default : Enum.Parse<TEnum>(v))
            .IsRequired(isRequired);
    }

    // Nullable enum to string
    public static PropertyBuilder<TEnum?> EnumColumn<TEntity, TEnum>(this EntityTypeBuilder<TEntity> builder, Expression<Func<TEntity, TEnum?>> column, bool isRequired = false)
        where TEntity : class, IEntity
        where TEnum : struct, Enum
    {
        return builder.Property(column)
            .HasConversion(
                (TEnum? v) => v.HasValue ? v.Value.ToString() : null,
                (string? v) => string.IsNullOrEmpty(v) ? (TEnum?)null : Enum.Parse<TEnum>(v))
            .IsRequired(isRequired);
    }

    // Non-nullable [Flags] enum as int
    public static PropertyBuilder<TEnum> FlagsEnumColumn<TEntity, TEnum>(this EntityTypeBuilder<TEntity> builder, Expression<Func<TEntity, TEnum>> column, bool isRequired = true)
        where TEntity : class, IEntity
        where TEnum : struct, Enum
    {
        return builder.Property(column)
            .HasConversion<int>()
            .IsRequired(isRequired);
    }

    // Nullable [Flags] enum as int
    public static PropertyBuilder<TEnum?> FlagsEnumColumn<TEntity, TEnum>(this EntityTypeBuilder<TEntity> builder, Expression<Func<TEntity, TEnum?>> column, bool isRequired = false)
        where TEntity : class, IEntity
        where TEnum : struct, Enum
    {
        return builder.Property(column)
            .HasConversion<int?>()
            .IsRequired(isRequired);
    }
}