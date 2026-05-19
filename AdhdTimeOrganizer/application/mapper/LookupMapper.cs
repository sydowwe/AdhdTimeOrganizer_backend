using AdhdTimeOrganizer.application.dto.request.generic;
using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.application.mapper.@interface;
using AdhdTimeOrganizer.domain.model.entity.@base.core;

namespace AdhdTimeOrganizer.application.mapper;

public class LookupMapper<T> :
    IBaseCreateMapper<T, SelectOptionRequest>,
    IBaseUpdateMapper<T, SelectOptionRequest>,
    IBaseResponseMapper<T, LookupResponse>
    where T : BaseLookup
{
    public LookupResponse ToResponse(T entity) =>
        new() { Id = entity.Id, Text = entity.Text, SortOrder = entity.SortOrder };

    public IQueryable<LookupResponse> ProjectToResponse(IQueryable<T> source) =>
        source.Select(e => new LookupResponse { Id = e.Id, Text = e.Text, SortOrder = e.SortOrder });

    public T ToEntity(SelectOptionRequest request, long userId)
    {
        var entity = (T)Activator.CreateInstance(typeof(T))!;
        entity.Text = request.Text;
        entity.SortOrder = request.SortOrder ?? 999;
        entity.UserId = userId;
        return entity;
    }

    public void UpdateEntity(SelectOptionRequest request, T entity)
    {
        entity.Text = request.Text;
        entity.SortOrder = request.SortOrder ?? entity.SortOrder;
    }
}
