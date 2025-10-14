using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.infrastructure.persistence.configuration.activityPlanning;

public class TemplateTaskConfiguration : IEntityTypeConfiguration<TemplateTask>
{
    public void Configure(EntityTypeBuilder<TemplateTask> builder)
    {
        builder.BaseEntityConfigure();
        builder.IsManyWithOneUser();
        builder.IsManyWithOneActivity();

        builder.Property(t => t.StartTime).IsRequired();
        builder.Property(t => t.EndTime).IsRequired();
        builder.Property(t => t.IsBackground).IsRequired();
        builder.Property(t => t.IsOptional).IsRequired();

        builder.Property(t => t.Location).HasMaxLength(200);
        builder.Property(t => t.Notes).HasMaxLength(1000);

        builder.HasOne(t => t.Template)
            .WithMany(tp => tp.Tasks)
            .HasForeignKey(t => t.TemplateId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(t => t.Priority)
            .WithMany()
            .HasForeignKey(t => t.PriorityId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(t => new { t.TemplateId, t.StartTime });
    }
}
