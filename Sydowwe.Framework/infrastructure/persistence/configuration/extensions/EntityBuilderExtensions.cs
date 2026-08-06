using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sydowwe.Framework.domain.entity.@base;
using Sydowwe.Framework.domain.entityInterface;
using Sydowwe.Framework.infrastructure.persistence.converter;
using Sydowwe.Framework.infrastructure.persistence.encryption;
using Helper = Sydowwe.Framework.domain.helper.Helper;

namespace Sydowwe.Framework.infrastructure.persistence.configuration.extensions;

public static class EntityBuilderExtensions
{
    public static void BaseEntityConfigure<TEntity>(this EntityTypeBuilder<TEntity> builder) where TEntity : class, IBaseTableEntity
    {
        var tableName = Helper.FromPascalCaseToSnakeCase(typeof(TEntity).Name.Replace("Read", ""));
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

    public static PropertyBuilder<object?> PriceColumn<TEntity>(this EntityTypeBuilder<TEntity> builder, Expression<Func<TEntity, object?>> column, bool isRequired = true)
        where TEntity : BaseEntity => builder.Property(column).HasColumnType("decimal(18,2)").IsRequired(isRequired);

    public static PropertyBuilder<TEnum> EnumColumn<TEntity, TEnum>(this EntityTypeBuilder<TEntity> builder, Expression<Func<TEntity, TEnum>> column, bool isRequired = true)
        where TEntity : class, IEntity
        where TEnum : struct, Enum
    {
        return builder.Property(column)
            .HasConversion(
                v => v.ToString(),
                v => string.IsNullOrEmpty(v) ? default : Enum.Parse<TEnum>(v))
            .IsRequired(isRequired);
    }

    // Nullable enum to string
    public static PropertyBuilder<TEnum?> EnumColumn<TEntity, TEnum>(this EntityTypeBuilder<TEntity> builder, Expression<Func<TEntity, TEnum?>> column, bool isRequired = false)
        where TEntity : class, IEntity
        where TEnum : struct, Enum
    {
        return builder.Property(column)
            .HasConversion(
                v => v.HasValue ? v.Value.ToString() : null,
                v => string.IsNullOrEmpty(v) ? (TEnum?)null : Enum.Parse<TEnum>(v))
            .IsRequired(isRequired);
    }

    // Non-nullable [Flags] enum as int
    public static PropertyBuilder<TEnum> FlagsEnumColumn<TEntity, TEnum>(this EntityTypeBuilder<TEntity> builder, Expression<Func<TEntity, TEnum>> column, bool isRequired = true)
        where TEntity : class, IEntity
        where TEnum : struct, Enum =>
        builder.Property(column)
            .HasConversion<int>()
            .IsRequired(isRequired);

    // Nullable [Flags] enum as int
    public static PropertyBuilder<TEnum?> FlagsEnumColumn<TEntity, TEnum>(this EntityTypeBuilder<TEntity> builder, Expression<Func<TEntity, TEnum?>> column, bool isRequired = false)
        where TEntity : class, IEntity
        where TEnum : struct, Enum =>
        builder.Property(column)
            .HasConversion<int?>()
            .IsRequired(isRequired);

    /// <summary>
    /// Stores a string property AES-256-GCM-encrypted at rest (GDPR Art. 32). The column holds a
    /// versioned encryption token, not plaintext — so it cannot be filtered on or made unique
    /// (randomized encryption). Use only for high-sensitivity fields read by row id (e.g. national
    /// identifiers, IBAN). Column type is <c>text</c> since ciphertext is longer than the plaintext.
    /// </summary>
    public static PropertyBuilder<string> EncryptedColumn<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        Expression<Func<TEntity, string>> column,
        bool isRequired = true) where TEntity : class =>
        builder.Property(column)
            .HasConversion(new EncryptedStringConverter(AesGcmFieldEncryptor.Shared))
            .HasColumnType("text")
            .IsRequired(isRequired);

    public static PropertyBuilder<string?> StoredComputedColumn<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        Expression<Func<TEntity, string?>> column,
        string sql) where TEntity : class =>
        builder.Property(column).HasComputedColumnSql(sql, true);

    public static PropertyBuilder<TProperty> StoredComputedColumn<TEntity, TProperty>(
        this EntityTypeBuilder<TEntity> builder,
        Expression<Func<TEntity, TProperty>> column,
        string sql) where TEntity : class =>
        builder.Property(column).HasComputedColumnSql(sql, true);
}