using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sydowwe.Framework.domain.entity.@base;
using Sydowwe.Framework.domain.entity.user;
using Sydowwe.Framework.infrastructure.persistence.configuration.extensions;

namespace Sydowwe.Framework.infrastructure.persistence.configuration;

/// <summary>
/// User-scoped counterpart to <see cref="BaseLookupConfiguration{T}"/>: adds the user FK and moves
/// the uniqueness constraint from <c>Text</c> to <c>(UserId, Text)</c>, so each user owns their own
/// set of values.
/// </summary>
public class BaseLookupWithUserConfiguration<TUser, T> : IEntityTypeConfiguration<T>
    where TUser : BaseUser
    where T : BaseLookupWithUser<TUser>
{
    public void Configure(EntityTypeBuilder<T> e)
    {
        e.BaseEntityConfigure();
        e.IsManyWithOneUser<TUser, T>();

        e.Property(x => x.Text).IsRequired().HasMaxLength(100);
        e.Property(x => x.SortOrder).IsRequired();

        e.HasIndex(x => new { x.UserId, x.Text }).IsUnique();
    }
}
