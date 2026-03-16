using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence.configuration.extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.infrastructure.persistence.configuration.todoList;

public class TodoListItemConfiguration : IEntityTypeConfiguration<TodoListItem>
{
    public void Configure(EntityTypeBuilder<TodoListItem> builder)
    {
        builder.BaseEntityConfigure();

        builder.BaseTodoListConfigure();

        builder.IsManyWithOneUser(u => u.TodoListItemColl);
        builder.IsManyWithOneActivity(a => a.TodoListItems);

        builder.HasOne(r => r.TaskPriority)
            .WithMany(t => t.TodoListColl)
            .HasForeignKey(r => r.TaskPriorityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(t => new { userId = t.UserId, activityId = t.ActivityId })
            .IsUnique();

        builder.HasIndex(t => new { userId = t.UserId, TaskPriorityId = t.TaskPriorityId });
    }
}
