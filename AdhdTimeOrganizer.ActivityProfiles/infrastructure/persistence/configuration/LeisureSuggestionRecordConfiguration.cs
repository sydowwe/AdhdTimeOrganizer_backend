using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.Core.infrastructure.persistence.configuration.extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sydowwe.Framework.infrastructure.persistence.configuration.extensions;

namespace AdhdTimeOrganizer.ActivityProfiles.infrastructure.persistence.configuration;

public class LeisureSuggestionRecordConfiguration : IEntityTypeConfiguration<LeisureSuggestionRecord>
{
    public void Configure(EntityTypeBuilder<LeisureSuggestionRecord> builder)
    {
        builder.BaseEntityConfigure();

        // Parameterless, like MemoryAnchor's: neither User nor Activity carries an inverse collection in
        // this direction, and adding one would make Core reference this slice. Both FKs still cascade, so a
        // deleted activity takes its draw history with it — a record that outlived the activity it names
        // would be an untouchable row keeping a key alive that no draw can ever produce again.
        builder.IsManyWithOneUser();
        builder.IsManyWithOneActivity();

        builder.EnumColumn(r => r.Source);
        builder.EnumColumn(r => r.LastOutcome);
        builder.Property(r => r.LastSuggestedAt).IsRequired();

        // The natural key. Unique because the record is an upsert target — "when was this last shown" has
        // exactly one answer — and a duplicate would make staleness depend on which row the read happened
        // to see. It also leads on UserId, which is what the retention sweep and every read filter on.
        builder.HasIndex(r => new { r.UserId, r.Source, r.ActivityId }).IsUnique();
    }
}
