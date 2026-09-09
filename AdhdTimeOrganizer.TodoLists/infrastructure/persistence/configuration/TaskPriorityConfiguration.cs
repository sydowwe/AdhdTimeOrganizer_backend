using AdhdTimeOrganizer.Core.infrastructure.persistence.configuration.extensions;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sydowwe.Framework.infrastructure.persistence.configuration.extensions;

namespace AdhdTimeOrganizer.TodoLists.infrastructure.persistence.configuration.todoList;

public class TaskPriorityConfiguration : IEntityTypeConfiguration<TaskPriority>
{
    public void Configure(EntityTypeBuilder<TaskPriority> builder)
    {
        builder.BaseTextColorEntityConfigure();
        builder.IsManyWithOneUser();
        builder.Property(t => t.Priority).IsRequired();

        // The TaskPriority -> TodoListItem relationship is configured once, from the dependent side,
        // in TodoListItemConfiguration (HasOne(r => r.TaskPriority).WithMany(t => t.TodoListColl),
        // Restrict). It used to be declared here as well; two declarations of one relationship is
        // redundant and invites the two copies drifting apart on delete behaviour.

        // Every read of this lookup is user-scoped first, so the composite serves the sort too.
        // A bare (priority) index could never be chosen and only cost writes.
        builder.HasIndex(t => new { t.UserId, t.Priority }).IsUnique();
    }
}