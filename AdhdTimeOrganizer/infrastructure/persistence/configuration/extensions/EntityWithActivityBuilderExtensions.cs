using System.Linq.Expressions;
using AdhdTimeOrganizer.domain.model.entity.activity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.infrastructure.persistence;

public static class EntityWithActivityBuilderExtensions
{
    public static void IsManyWithOneActivity<TEntity>(this EntityTypeBuilder<TEntity> builder, Expression<Func<Activity, IEnumerable<TEntity>?>>? navigationProperty = null, DeleteBehavior deleteBehavior = DeleteBehavior.Cascade, bool isRequired = true) where TEntity : BaseEntityWithActivity
    {
        builder.HasOne(p => p.Activity)
            .WithMany(navigationProperty)
            .HasForeignKey(p => p.ActivityId)
            .IsRequired(isRequired)
            .OnDelete(deleteBehavior);
    }
    public static void IsOneWithOneActivity<TEntity>(this EntityTypeBuilder<TEntity> builder, Expression<Func<Activity, TEntity?>>? navigationProperty = null, DeleteBehavior deleteBehavior = DeleteBehavior.Cascade, bool isRequired = true) where TEntity : BaseEntityWithActivity
    {
        builder.HasOne(p => p.Activity)
            .WithOne(navigationProperty)
            .HasForeignKey<TEntity>(p => p.ActivityId)
            .IsRequired(isRequired)
            .OnDelete(deleteBehavior);
    }
}