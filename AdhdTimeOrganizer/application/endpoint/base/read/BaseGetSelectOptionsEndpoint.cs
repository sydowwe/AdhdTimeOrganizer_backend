using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.application.helper;
using AdhdTimeOrganizer.application.mapper.@interface;
using AdhdTimeOrganizer.domain.model.entity.user;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Humanizer;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.@base.read;

public abstract class BaseGetSelectOptionsEndpoint<TEntity, TMapper>(AppCommandDbContext appDbContext, TMapper mapper) : EndpointWithoutRequest<List<SelectOptionResponse>>
    where TEntity : class, IEntityWithUser
    where TMapper : IBaseSelectOptionMapper<TEntity>
{
    private readonly TMapper _mapper = mapper;

    public virtual string[] AllowedRoles()
    {
        return EndpointHelper.GetUserOrHigherRoles();
    }

    public virtual string AddedRouteParam => string.Empty;

    public virtual bool FilteredByUser => true;

    public override void Configure()
    {
        var entityName = typeof(TEntity).Name;
        Get($"/{entityName.Kebaberize()}/all-options/{AddedRouteParam}");
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

        if (FilteredByUser)
        {
            query = query.FilteredByUser(User.GetId());
        }

        query = Filter(query);

        query = Sort(query);

        var options = await query.Select(e => _mapper.ToSelectOptionResponse(e)).ToListAsync(ct);

        await SendOkAsync(options, ct);
    }

    public virtual IQueryable<TEntity> Filter(IQueryable<TEntity> query) => query;

    protected virtual IQueryable<TEntity> Sort(IQueryable<TEntity> query) => query.OrderBy(e => e.Id);

}