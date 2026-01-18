using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.infrastructure.persistence.configuration.activityPlanning;

public class TemplateTaskConfiguration : IEntityTypeConfiguration<TemplatePlannerTask>
{
    public void Configure(EntityTypeBuilder<TemplatePlannerTask> builder)
    {
        builder.BaseEntityConfigure();
        builder.IsManyWithOneUser();
        builder.IsManyWithOneActivity();

        builder.Property(t => t.StartTime).IsRequired();
        builder.Property(t => t.EndTime).IsRequired();
        builder.Property(t => t.IsBackground).IsRequired();

        builder.Property(t => t.Location).HasMaxLength(200);
        builder.Property(t => t.Notes).HasMaxLength(1000);

        builder.HasOne(t => t.Template)
            .WithMany(tp => tp.Tasks)
            .HasForeignKey(t => t.TemplateId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(t => t.Importance)
            .WithMany()
            .HasForeignKey(t => t.ImportanceId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(t => new { t.TemplateId, t.StartTime });
    }
}
