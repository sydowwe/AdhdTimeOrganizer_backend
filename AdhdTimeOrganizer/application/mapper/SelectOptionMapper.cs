using System.Runtime.CompilerServices;
using AdhdTimeOrganizer.application.dto.request.generic;
using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.application.mapper.@interface;
using AdhdTimeOrganizer.domain.model.entity;

namespace AdhdTimeOrganizer.application.mapper;

public class SelectOptionMapper<TEntity> : IBaseCreateMapper<TEntity, SelectOptionRequest>, IBaseUpdateMapper<TEntity, SelectOptionRequest>, IBaseReadMapper<TEntity, SelectOptionResponse>
    where TEntity : SelectOptionBase
{
    public SelectOptionResponse ToResponse(TEntity entity)
    {
        return new SelectOptionResponse
        {
            Id = entity.Id,
            Text = entity.Text,
        };
    }

    public TEntity ToEntity(SelectOptionRequest request, long userId)
    {
        var entity = RuntimeHelpers.GetUninitializedObject(typeof(TEntity));
        var typedEntity = (TEntity)entity;

        // Set the properties
        typedEntity.Text = request.Text;
        typedEntity.SortOrder = request.SortOrder ?? 999;

        return typedEntity;
    }

    public void UpdateEntity(SelectOptionRequest request, TEntity entity)
    {
        entity.Text = request.Text;
        entity.SortOrder = request.SortOrder ?? 999;
    }

    public SelectOptionResponse ToSelectOptionResponse(TEntity entity)
    {
        return new SelectOptionResponse(entity.Id, entity.Text);
    }
}