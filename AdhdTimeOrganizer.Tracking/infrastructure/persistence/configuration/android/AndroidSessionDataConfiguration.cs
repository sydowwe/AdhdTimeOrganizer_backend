using AdhdTimeOrganizer.Core.infrastructure.persistence.configuration.extensions;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sydowwe.Framework.infrastructure.persistence.configuration.extensions;

namespace AdhdTimeOrganizer.Tracking.infrastructure.persistence.configuration.activityTracking.android;

public class AndroidSessionDataConfiguration : IEntityTypeConfiguration<AndroidSessionData>
{
    public void Configure(EntityTypeBuilder<AndroidSessionData> builder)
    {
        builder.BaseEntityConfigure();

        builder.Property(x => x.PackageName).HasMaxLength(255).IsRequired();
        builder.Property(x => x.AppLabel).HasMaxLength(255).IsRequired();
        builder.Property(x => x.DeviceId).HasMaxLength(100).IsRequired();

        builder.IsManyWithOneUser();

        builder.HasIndex(x => new { x.UserId, x.DeviceId, x.PackageName, x.SessionStartUtc }).IsUnique();
        builder.HasIndex(x => new { x.UserId, x.SessionStartUtc });
        builder.HasIndex(x => new { x.UserId, x.PackageName });

        // This is the one ledger that is not RANGE-partitioned, so the retention purge's
        // `SessionStartUtc < cutoff` (PurgeExpiredActivityTrackingEntriesJobHandler) is a real
        // cross-user sweep with no partition pruning to help it. The composite above is useless to
        // it — user_id leads — so the sweep needs session_start_utc on its own.
        builder.HasIndex(x => x.SessionStartUtc);

        // AndroidStackedBarsEndpoint predicates on `SessionStartUtc < envelopeTo && SessionEndUtc >
        // envelopeFrom`. Only the start side was indexed, so the overlap read walked everything
        // before the envelope's end and filtered.
        builder.HasIndex(x => new { x.UserId, x.SessionEndUtc });
    }
}