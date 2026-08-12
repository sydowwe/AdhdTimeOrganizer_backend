using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sydowwe.Framework.infrastructure.persistence.configuration.extensions;

namespace AdhdTimeOrganizer.TodoLists.infrastructure.persistence.configuration.todoList;

public class TaskPriorityConfiguration : IEntityTypeConfiguration<TaskPriority>
{
    public void Configure(EntityTypeBuilder<TaskPriority> builder)
    {
        builder.BaseTextColorEntityConfigure();
        builder.Property(t => t.Priority).IsRequired();

        // The TaskPriority -> TodoListItem relationship is configured once, from the dependent side,
        // in TodoListItemConfiguration (HasOne(r => r.TaskPriority).WithMany(t => t.TodoListColl),
        // Restrict). It used to be declared here as well; two declarations of one relationship is
        // redundant and invites the two copies drifting apart on delete behaviour.

        builder.HasIndex(t => new { t.UserId, t.Priority }).IsUnique();

        // Add index for sorting performance
        builder.HasIndex(t => t.Priority);
    }
}