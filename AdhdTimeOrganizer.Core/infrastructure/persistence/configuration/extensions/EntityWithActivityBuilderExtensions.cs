using System.Linq.Expressions;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.Core.infrastructure.persistence.configuration.extensions;

public static class EntityWithActivityBuilderExtensions
{
    extension<TEntity>(EntityTypeBuilder<TEntity> builder) where TEntity : BaseEntityWithActivity
    {
        public ReferenceCollectionBuilder<Activity, TEntity> IsManyWithOneActivity(Expression<Func<Activity, IEnumerable<TEntity>?>>? navigationProperty = null,
            DeleteBehavior deleteBehavior = DeleteBehavior.Cascade)
        {
            return builder.HasOne(p => p.Activity)
                .WithMany(navigationProperty)
                .HasForeignKey(p => p.ActivityId)
                .IsRequired()
                .OnDelete(deleteBehavior);
        }

        public ReferenceReferenceBuilder<TEntity, Activity> IsOneWithOneActivity(Expression<Func<Activity, TEntity?>>? navigationProperty = null,
            DeleteBehavior deleteBehavior = DeleteBehavior.Cascade)
        {
            return builder.HasOne(p => p.Activity)
                .WithOne(navigationProperty)
                .HasForeignKey<TEntity>(p => p.ActivityId)
                .IsRequired()
                .OnDelete(deleteBehavior);
        }
    }
}