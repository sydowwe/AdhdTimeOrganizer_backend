using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.infrastructure.persistence.configuration.activityPlanning;

public class TaskUrgencyConfiguration : IEntityTypeConfiguration<TaskUrgency>
{
    public void Configure(EntityTypeBuilder<TaskUrgency> builder)
    {
        builder.BaseTextColorEntityConfigure();
        builder.Property(t=>t.Priority).IsRequired();

        // Configure the relationship
        builder.HasMany(t => t.TodoListColl)
            .WithOne(tl => tl.TaskUrgency)
            .HasForeignKey(tl => tl.TaskUrgencyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(t => new { t.UserId, t.Priority }).IsUnique();

        // Add index for sorting performance
        builder.HasIndex(t => t.Priority);
    }
}