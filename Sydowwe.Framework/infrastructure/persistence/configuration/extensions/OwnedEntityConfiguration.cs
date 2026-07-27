using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Sydowwe.Framework.infrastructure.persistence.configuration.extensions;

public static class OwnedEntityConfiguration
{
    private const string AddressSql =
        "(address->>'street') || ' ' || (address->>'house_number') || ', ' || " +
        "(address->>'city') || ' ' || (address->>'postal_code')";


    public static void ComputedAddress<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        Expression<Func<TEntity, string?>> column) where TEntity : class
    {
        builder.StoredComputedColumn(column,
            $"CASE WHEN address IS NULL THEN NULL ELSE {AddressSql} END");
    }

    public static OwnedNavigationBuilder<TOwner, TOwned> Json<TOwner, TOwned>(
        this OwnedNavigationBuilder<TOwner, TOwned> builder)
        where TOwner : class
        where TOwned : class
    {
        builder.ToJson();

        foreach (var property in typeof(TOwned).GetProperties())
            builder.Property(property.Name).HasJsonPropertyName(ToSnakeCase(property.Name));

        return builder;
    }

    private static string ToSnakeCase(string name)
    {
        return string.Concat(name.Select((c, i) =>
            i > 0 && char.IsUpper(c) ? "_" + char.ToLower(c) : char.ToLower(c).ToString()));
    }
}