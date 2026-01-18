using AdhdTimeOrganizer.application.dto.request.@base.table;
using AdhdTimeOrganizer.application.dto.request.@interface;
using AdhdTimeOrganizer.application.dto.response.@base;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.application.helper;
using AdhdTimeOrganizer.application.mapper.@interface;
using AdhdTimeOrganizer.domain.model.entity.user;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Humanizer;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.@base.read.pageFilterSort;

public abstract class BaseFilterSortEndpoint<TEntity, TResponse, TFilter, TMapper>(
    AppCommandDbContext dbContext,
    TMapper mapper) : Endpoint<BaseFilterSortRequest<TFilter>, List<TResponse>>
    where TEntity : class, IEntityWithUser
    where TResponse : class, IIdResponse
    where TFilter : class, IFilterRequest
    where TMapper : class, IBaseReadMapper<TEntity, TResponse>
{
    public virtual string EndpointPath => "filter-sort";

    public virtual string[] AllowedRoles()
    {
        return EndpointHelper.GetUserOrHigherRoles();
    }

    public virtual bool FilteredByUser => true;

    public override void Configure()
    {
        var entityName = typeof(TEntity).Name;
        Post($"/{entityName.Kebaberize()}/{EndpointPath}");
        Summary(s =>
        {
            s.Summary = $"Get filtered and paginated {entityName} list";
            s.Description = $"Retrieves a filtered, paginated and sorted list of {entityName}";
            Roles(AllowedRoles());

            s.Response<BaseTableResponse<TResponse>>(200, "Success");
            s.Response(400, "Bad request");
        });
    }

    public override async Task HandleAsync(BaseFilterSortRequest<TFilter> req, CancellationToken ct)
    {
        try
        {
            var query = WithIncludes(dbContext.Set<TEntity>().AsNoTracking());

            if (FilteredByUser)
            {
                query = query.FilteredByUser(User.GetId());
            }

            if (req is { UseFilter: true, Filter: not null })
            {
                query = ApplyCustomFiltering(query, req.Filter);
            }

            var response = await mapper.ProjectToResponse(query.SortByMany(req.SortBy)).ToListAsync(ct);

            await SendOkAsync(response, ct);
        }
        catch (Exception ex)
        {
            AddError($"An error occurred while retrieving filtered data: {ex.Message}");
            await SendErrorsAsync(500, ct);
        }
    }

    protected abstract IQueryable<TEntity> ApplyCustomFiltering(IQueryable<TEntity> query, TFilter filter);

    protected virtual IQueryable<TEntity> WithIncludes(IQueryable<TEntity> query)
    {
        return query;
    }
}