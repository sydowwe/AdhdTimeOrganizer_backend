using System.Linq.Expressions;
using AdhdTimeOrganizer.Common.domain.model.entity;
using AdhdTimeOrganizer.Common.domain.model.entityInterface;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Helper = AdhdTimeOrganizer.Common.domain.helper.Helper;

namespace AdhdTimeOrganizer.Common.infrastructure.persistence;

public static class EntityBuilderExtensions
{
    public static void BaseEntityConfigure<TEntity>(this EntityTypeBuilder<TEntity> builder) where TEntity : BaseEntity
    {

        var tableName = Helper.FromPascalCaseToSnakeCase(typeof(TEntity).Name.Replace("Read", ""));
        builder.ToTable(tableName);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).UseSerialColumn();

        builder.Property<uint>("row_version")
            .IsConcurrencyToken()
            .IsRowVersion();
        builder.Property(x => x.CreatedTimestamp)
            .IsRequired();
        builder.Property(x => x.ModifiedTimestamp)
            .IsRequired();
    }

    public static PropertyBuilder<object> PriceColumn<TEntity>(this EntityTypeBuilder<TEntity> builder, Expression<Func<TEntity, object>> column, bool isRequired = true) where TEntity : BaseEntity
    {
        return builder.Property(column).HasColumnType("decimal(18,2)").IsRequired(isRequired);
    }

    //TODO maybe fix the conversion
    public static PropertyBuilder<TEnum> EnumColumn<TEntity, TEnum>(this EntityTypeBuilder<TEntity> builder, Expression<Func<TEntity, TEnum>> column, bool isRequired = true)
        where TEntity : class, IEntity
        where TEnum : struct, Enum
    {
        return builder.Property(column)
            .HasConversion(
                v => v.ToString(),
                v => string.IsNullOrEmpty(v) ? default : Enum.Parse<TEnum>(v)
            ).IsRequired(isRequired);
    }
}