using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence.configuration.extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.infrastructure.persistence.configuration.activityPlanning;

public class TodoListConfiguration : IEntityTypeConfiguration<TodoList>
{
    public void Configure(EntityTypeBuilder<TodoList> builder)
    {
        builder.BaseEntityConfigure();

        builder.BaseTodoListConfigure();

        builder.IsManyWithOneUser(u => u.TodoListColl);
        builder.IsOneWithOneActivity(a=>a.TodoList);

        builder.HasOne(r => r.TaskPriority)
            .WithMany(t=>t.TodoListColl)
            .HasForeignKey(r => r.TaskPriorityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(t => new { userId = t.UserId, activityId = t.ActivityId })
            .IsUnique();

        builder.HasIndex(t => new { userId = t.UserId, TaskPriorityId = t.TaskPriorityId });
    }
}