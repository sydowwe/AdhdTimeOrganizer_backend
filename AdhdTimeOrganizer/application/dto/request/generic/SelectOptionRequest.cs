using AdhdTimeOrganizer.application.dto.request.@interface;
using AdhdTimeOrganizer.domain.model.entity.activity.lookup;
using AdhdTimeOrganizer.domain.model.entity.@base.core;

namespace AdhdTimeOrganizer.application.dto.request.generic;

public record SelectOptionRequest<TEntity>(string Text, int? SortOrder)
    : IMyRequest<TEntity> where TEntity : BaseLookup, new()
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