using AdhdTimeOrganizer.domain.model.entity.todoList;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.infrastructure.persistence.configuration.activityPlanning;

public class TaskPriorityConfiguration : IEntityTypeConfiguration<TaskPriority>
{
    public void Configure(EntityTypeBuilder<TaskPriority> builder)
    {
        builder.BaseTextColorEntityConfigure();
        builder.Property(t=>t.Priority).IsRequired();

        // Configure the relationship
        builder.HasMany(t => t.TodoListColl)
            .WithOne(tl => tl.TaskPriority)
            .HasForeignKey(tl => tl.TaskPriorityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(t => new { t.UserId, t.Priority }).IsUnique();

        // Add index for sorting performance
        builder.HasIndex(t => t.Priority);
    }
}