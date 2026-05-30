using AdhdTimeOrganizer.application.dto.request.@base.table;
using AdhdTimeOrganizer.application.dto.request.generic;
using AdhdTimeOrganizer.application.dto.request.@interface;
using AdhdTimeOrganizer.application.dto.response;
using AdhdTimeOrganizer.application.dto.response.@base;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.application.helper;
using AdhdTimeOrganizer.domain.model.entity.user;
using AdhdTimeOrganizer.domain.model.entityInterface;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Humanizer;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.@base.read.pageFilterSort;

public abstract class BaseFilterSortEndpoint<TEntity, TResponse, TFilter>(AppDbContext dbContext)
    : Endpoint<BaseFilterSortRequest<TFilter>, List<TResponse>>
    where TEntity : class, IEntityWithUser, IEntityWithId
    where TResponse : class, IIdResponse, IProjectionResponse<TResponse, TEntity>
    where TFilter : class, IFilterRequest
{
    public virtual string[] AllowedRoles() => EndpointHelper.GetAdminOrHigherRoles();

    public virtual string EndpointPath => "filter-sort";

    public virtual bool FilteredByUser => true;

    public virtual SortByRequest[] AlwaysSortBy => [];

    public override void Configure()
    {
        var entityName = typeof(TEntity).Name;
        Post($"/{entityName.Kebaberize()}/{EndpointPath}");
        Roles(AllowedRoles());

        Summary(s =>
        {
            s.Summary = $"Get filtered and sorted {entityName} list";
            s.Description = $"Retrieves a filtered and sorted list of {entityName}";

            s.Response<List<TResponse>>(200, "Success");
            s.Response(400, "Bad request");
        });
    }

    public override async Task HandleAsync(BaseFilterSortRequest<TFilter> req, CancellationToken ct)
    {
        try
        {
            var query = dbContext.Set<TEntity>().AsNoTracking();

            if (FilteredByUser)
            {
                query = query.FilteredByUser(User.GetId());
            }

            if (req is { UseFilter: true, Filter: not null })
            {
                query = ApplyCustomFiltering(query, req.Filter);
            }

            var sortBy = AlwaysSortBy.Concat(req.SortBy).ToArray();

            var response = await TResponse.Projection(query).SortByMany(sortBy).ToListAsync(ct);

            await Send.OkAsync(response, ct);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error retrieving filter-sorted data for {Entity}", typeof(TEntity).Name);
            AddError("An internal error occurred.");
            await Send.ErrorsAsync(500, ct);
        }
    }

    protected abstract IQueryable<TEntity> ApplyCustomFiltering(IQueryable<TEntity> query, TFilter filter);
}