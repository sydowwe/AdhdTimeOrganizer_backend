using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sydowwe.Framework.Contracts.user;
using Sydowwe.Framework.infrastructure.persistence.configuration.extensions;

namespace MojaDigitalnaFirma.Core.infrastructure.persistence.configuration.extensions;

public static class EntityWithCoreUserBuilderExtensions
{
    public static ReferenceCollectionBuilder<CoreUser, TEntity> IsManyWithOneCoreUser<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        Expression<Func<CoreUser, IEnumerable<TEntity>?>>? navigationProperty = null,
        DeleteBehavior deleteBehavior = DeleteBehavior.Cascade)
        where TEntity : BaseEntityWithCoreUser
        => builder.IsManyWithOneUser(navigationProperty, deleteBehavior);

    public static ReferenceReferenceBuilder<TEntity, CoreUser> IsOneWithOneCoreUser<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        Expression<Func<CoreUser, TEntity?>>? navigationProperty = null,
        DeleteBehavior deleteBehavior = DeleteBehavior.Cascade)
        where TEntity : BaseEntityWithCoreUser
        => builder.IsOneWithOneUser(navigationProperty, deleteBehavior);
}