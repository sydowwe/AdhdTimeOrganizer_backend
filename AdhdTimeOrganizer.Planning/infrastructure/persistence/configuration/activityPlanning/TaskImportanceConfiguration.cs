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

        builder.HasIndex(t => new { t.UserId, t.Importance }).IsUnique();

        // Add index for sorting performance
        builder.HasIndex(t => t.Importance);
    }
}