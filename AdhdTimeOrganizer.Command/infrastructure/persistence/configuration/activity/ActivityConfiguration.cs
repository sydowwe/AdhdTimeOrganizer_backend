using AdhdTimeOrganizer.Command.domain.model.entity.activity;
using AdhdTimeOrganizer.Common.infrastructure.persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.Command.infrastructure.persistence.configuration;

public class ActivityConfiguration : IEntityTypeConfiguration<Activity>
{
    public void Configure(EntityTypeBuilder<Activity> builder)
    {
        builder.BaseNameTextEntityConfigure();

        builder.Property(x => x.IsOnToDoList).IsRequired();
        builder.Property(x => x.IsUnavoidable).IsRequired();

        builder.IsManyWithOneUser(u => u.ActivityList);

        builder.HasOne(e => e.Role)
            .WithMany(r => r.Activities)
            .HasForeignKey(e => e.RoleId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        builder.HasOne(e => e.Category)
            .WithMany(r => r.Activities)
            .HasForeignKey(e => e.CategoryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}