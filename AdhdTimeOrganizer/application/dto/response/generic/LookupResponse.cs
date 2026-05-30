using AdhdTimeOrganizer.domain.model.entity.@base.core;

namespace AdhdTimeOrganizer.application.dto.response.generic;

public record LookupResponse<TEntity> : SelectOptionResponse, IProjectionResponse<LookupResponse<TEntity>, TEntity> where TEntity : BaseLookup
{
    public int? SortOrder { get; init; }

    public static IQueryable<LookupResponse<TEntity>> Projection(IQueryable<TEntity> query) => query.Select(e => FromEntity(e));

    public static LookupResponse<TEntity> FromEntity(TEntity entity) => new()
    {
        Id = entity.Id,
        Text = entity.Text,
        SortOrder = entity.SortOrder
    };
}