using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.infrastructure.persistence.configuration.activityPlanning;

public class TodoListConfiguration : IEntityTypeConfiguration<TodoList>
{
    public void Configure(EntityTypeBuilder<TodoList> builder)
    {
        builder.BaseEntityConfigure();

        builder.Property(p => p.IsDone).HasDefaultValue(false).IsRequired();
        builder.IsManyWithOneUser(u => u.TodoListColl);
        builder.IsOneWithOneActivity(a=>a.TodoList);

        builder.HasOne(r => r.TaskUrgency)
            .WithMany(t=>t.TodoListColl)
            .HasForeignKey(r => r.TaskUrgencyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(t => new { userId = t.UserId, activityId = t.ActivityId })
            .IsUnique();

        builder.HasIndex(t => new { userId = t.UserId, taskUrgencyId = t.TaskUrgencyId });
    }
}