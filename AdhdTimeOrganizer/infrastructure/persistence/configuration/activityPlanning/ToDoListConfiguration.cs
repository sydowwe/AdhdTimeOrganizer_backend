using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.infrastructure.persistence.configuration.activityPlanning;

public class TodoListConfiguration : IEntityTypeConfiguration<TodoList>
{
    public void Configure(EntityTypeBuilder<TodoList> builder)
    {
        builder.BaseEntityConfigure();


        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_TodoList_DoneCount_Min", "done_count >= 0");
            t.HasCheckConstraint("CK_TodoList_TotalCount_Range", "total_count >= 2 AND total_count <= 99");
            t.HasCheckConstraint("CK_TodoList_DoneCount_LessOrEqual_TotalCount", "done_count <= total_count");
        });

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