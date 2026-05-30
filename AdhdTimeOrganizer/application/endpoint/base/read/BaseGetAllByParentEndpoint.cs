using AdhdTimeOrganizer.application.dto.response;
using AdhdTimeOrganizer.application.dto.response.@base;
using AdhdTimeOrganizer.application.helper;
using AdhdTimeOrganizer.domain.model.entityInterface;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Humanizer;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.@base.read;

public abstract class BaseGetAllByParentEndpoint<TEntity, TResponse>(AppDbContext dbContext) : EndpointWithoutRequest<List<TResponse>>
    where TEntity : class, IEntityWithId
    where TResponse : class, IIdResponse, IProjectionResponse<TResponse, TEntity>
{
    public virtual string[] AllowedRoles() => EndpointHelper.GetAdminOrHigherRoles();

    protected abstract string ParentName { get; }

    public override void Configure()
    {
        var entityName = typeof(TEntity).Name;
        Get($"/{entityName.Kebaberize()}/by-{ParentName.Kebaberize()}/{{parentId:long}}");
        Roles(AllowedRoles());
    }

    protected virtual Task<bool> AuthorizeAsync(long parentId, CancellationToken ct) => Task.FromResult(true);

    protected abstract IQueryable<TEntity> FilterByParent(IQueryable<TEntity> query, long parentId);

    public override async Task HandleAsync(CancellationToken ct)
    {
        var parentId = Route<long>("parentId");

        if (!await AuthorizeAsync(parentId, ct))
        {
            await Send.ForbiddenAsync(ct);
            return;
        }

        var query = FilterByParent(dbContext.Set<TEntity>().AsNoTracking(), parentId);
        await Send.OkAsync(await TResponse.Projection(query).ToListAsync(ct), ct);
    }
}
