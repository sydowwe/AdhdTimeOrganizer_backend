using AdhdTimeOrganizer.domain.model.valueObject;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.infrastructure.persistence.configuration.extensions;

public static class OwnedEntityConfiguration
{
    public static void ConfigureOwnedAddress<T>(OwnedNavigationBuilder<T, Address> builder) where T : class
    {
        builder.Property(a => a.Street).HasMaxLength(100).IsUnicode().IsRequired();
        builder.Property(a => a.HouseNumber).IsRequired();
        builder.Property(a => a.City).HasMaxLength(100).IsUnicode().IsRequired();
        builder.Property(a => a.PostalCode).HasMaxLength(6).IsRequired();
    }
}