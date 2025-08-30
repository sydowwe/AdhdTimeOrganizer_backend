using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.application.mapper.@interface;
using AdhdTimeOrganizer.domain.helper;
using AdhdTimeOrganizer.domain.model.entityInterface;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Humanizer;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.@base.read;

public abstract class BaseGetSelectOptionsEndpoint<TEntity, TMapper>(AppCommandDbContext appDbContext, TMapper mapper) : EndpointWithoutRequest<List<SelectOptionResponse>>
    where TEntity : class, IEntityWithId
    where TMapper : IBaseSelectOptionMapper<TEntity>
{
    private readonly TMapper _mapper = mapper;

    public virtual string[] AllowedRoles()
    {
        return EndpointHelper.GetUserOrHigherRoles();
    }

    public virtual string AddedRouteParam()
    {
        return string.Empty;
    }

    public override void Configure()
    {
        var entityName = typeof(TEntity).Name;
        Get($"/{entityName.Kebaberize()}/all-options/{AddedRouteParam()}");
        Roles(AllowedRoles());
        Summary(s =>
        {
            s.Summary = $"Get {entityName} select options";
            s.Description = $"Retrieves {entityName} as select options (id and text)";
            s.Response<List<SelectOptionResponse>>(200, "Success");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var query = appDbContext.Set<TEntity>().AsNoTracking();
        query = Filter(query);
        var entities = await query.ToListAsync(ct);
        var options = entities.Select(e => _mapper.ToSelectOptionResponse(e)).ToList();

        await SendOkAsync(options, ct);
    }

    public virtual IQueryable<TEntity> Filter(IQueryable<TEntity> query) => query;
}