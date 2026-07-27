using Sydowwe.Framework.application.dto.request.@interface;
using Sydowwe.Framework.domain.entityInterface;

namespace Sydowwe.Framework.application.dto.request.generic;

/// <summary>
/// Create/update request for any lookup entity, user-scoped or not. Carries the mapping so it can
/// be handed straight to <c>BaseCreateEndpoint</c> / <c>BaseUpdateEndpoint</c>.
/// </summary>
public record LookupRequest<TEntity>(string Text, int? SortOrder)
    : IMyRequest<TEntity> where TEntity : class, IBaseLookupEntity, new()
{
    public void UpdateEntity(TEntity entity)
    {
        entity.Text = Text;
        entity.SortOrder = SortOrder;
    }

    public TEntity ToEntity => new()
    {
        Text = Text,
        SortOrder = SortOrder
    };
}
