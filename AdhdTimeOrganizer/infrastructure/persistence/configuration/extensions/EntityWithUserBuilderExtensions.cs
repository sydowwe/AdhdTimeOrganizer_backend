using System.Linq.Expressions;
using AdhdTimeOrganizer.domain.model.entity.user;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sydowwe.Framework.domain.entity.user;
using Sydowwe.Framework.infrastructure.persistence.configuration.extensions;

namespace AdhdTimeOrganizer.infrastructure.persistence.configuration.extensions;

/// <summary>
/// Closes Framework's generic <c>IsManyWithOneUser&lt;TUser, TEntity&gt;</c> /
/// <c>IsOneWithOneUser&lt;TUser, TEntity&gt;</c> over this portal's single <see cref="User"/> type.
/// C# can't infer <c>TUser</c> from a constraint, so without these shims every call site would have
/// to spell out both type arguments. The behaviour lives in Framework — don't reimplement it here.
/// </summary>
public static class EntityWithUserBuilderExtensions
{
    extension<TEntity>(EntityTypeBuilder<TEntity> builder) where TEntity : BaseEntityWithUser<User>
    {
        public ReferenceCollectionBuilder<User, TEntity> IsManyWithOneUser(Expression<Func<User, IEnumerable<TEntity>?>>? navigationProperty = null, DeleteBehavior deleteBehavior = DeleteBehavior.Cascade)
        {
            return builder.IsManyWithOneUser<User, TEntity>(navigationProperty, deleteBehavior);
        }

        public ReferenceReferenceBuilder<TEntity, User> IsOneWithOneUser(Expression<Func<User, TEntity?>>? navigationProperty = null,
            DeleteBehavior deleteBehavior = DeleteBehavior.Cascade)
        {
            return builder.IsOneWithOneUser<User, TEntity>(navigationProperty, deleteBehavior);
        }
    }
}