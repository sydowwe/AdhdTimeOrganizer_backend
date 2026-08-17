using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sydowwe.Framework.infrastructure.persistence.configuration;

namespace AdhdTimeOrganizer.ActivityProfiles.infrastructure.persistence.configuration;

/// <summary>
/// The one activity lookup that is not a bare <see cref="BaseLookupWithUserConfiguration{TUser,T}"/>: it carries
/// <see cref="ActivityWeatherDependency.Code"/> as well. The base's Configure is not virtual, so it is composed
/// rather than inherited — which keeps the table, key, row version, timestamps, user FK and the
/// <c>(user_id, text)</c> unique index coming from exactly one place, as everywhere else.
/// </summary>
public class ActivityWeatherDependencyConfiguration : IEntityTypeConfiguration<ActivityWeatherDependency>
{
    public void Configure(EntityTypeBuilder<ActivityWeatherDependency> builder)
    {
        new BaseLookupWithUserConfiguration<User, ActivityWeatherDependency>().Configure(builder);

        // Nullable and unindexed: a handful of rows per user are read whole on the one endpoint that cares, and
        // "no code" is a real state (a row the user invented) rather than missing data.
        builder.Property(x => x.Code).HasMaxLength(20);
    }
}
