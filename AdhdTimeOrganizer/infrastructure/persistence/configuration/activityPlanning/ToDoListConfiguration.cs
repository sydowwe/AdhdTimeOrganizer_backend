using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.infrastructure.persistence.configuration.activityPlanning;

public class ToDoListConfiguration : IEntityTypeConfiguration<ToDoList>
{
    public void Configure(EntityTypeBuilder<ToDoList> builder)
    {
        builder.BaseEntityConfigure();

        builder.Property(p => p.IsDone).HasDefaultValue(false).IsRequired();
        builder.IsManyWithOneUser(u => u.ToDoListColl);
        builder.IsOneWithOneActivity(a=>a.ToDoList);

        builder.HasOne(r => r.TaskUrgency)
            .WithMany(t=>t.ToDoListColl)
            .HasForeignKey(r => r.TaskUrgencyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(t => new { userId = t.UserId, activityId = t.ActivityId })
            .IsUnique();

        builder.HasIndex(t => new { userId = t.UserId, taskUrgencyId = t.TaskUrgencyId });
    }
}