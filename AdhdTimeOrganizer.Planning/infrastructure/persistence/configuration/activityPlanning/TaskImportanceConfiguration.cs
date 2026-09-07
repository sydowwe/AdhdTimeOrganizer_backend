using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sydowwe.Framework.infrastructure.persistence.configuration.extensions;

namespace AdhdTimeOrganizer.Planning.infrastructure.persistence.configuration.activityPlanning;

public class TaskImportanceConfiguration : IEntityTypeConfiguration<TaskImportance>
{
    public void Configure(EntityTypeBuilder<TaskImportance> builder)
    {
        builder.BaseTextColorIconEntityConfigure();
        builder.Property(t => t.Importance).IsRequired();

        // Serves both uniqueness and the only ordering anyone does — importance within a user. The bare
        // HasIndex(Importance) that used to sit alongside it claimed to be "for sorting performance"; it was
        // redundant with this composite's leading columns and unusable on its own, since every read is
        // user-scoped (PERF-5).
        builder.HasIndex(t => new { t.UserId, t.Importance }).IsUnique();
    }
}