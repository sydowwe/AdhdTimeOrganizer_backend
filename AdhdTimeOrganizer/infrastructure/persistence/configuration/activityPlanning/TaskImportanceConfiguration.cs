using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.infrastructure.persistence.configuration.activityPlanning;

public class TaskImportanceConfiguration : IEntityTypeConfiguration<TaskImportance>
{
    public void Configure(EntityTypeBuilder<TaskImportance> builder)
    {
        builder.BaseTextColorEntityConfigure();
        builder.Property(t=>t.Importance).IsRequired();

        builder.HasIndex(t => new { t.UserId, t.Importance }).IsUnique();

        // Add index for sorting performance
        builder.HasIndex(t => t.Importance);
    }
}
