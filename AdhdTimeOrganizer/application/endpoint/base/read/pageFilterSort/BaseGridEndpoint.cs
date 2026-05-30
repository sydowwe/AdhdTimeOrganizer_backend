using AdhdTimeOrganizer.application.dto.request.@base.table;
using AdhdTimeOrganizer.application.dto.request.@interface;
using AdhdTimeOrganizer.application.dto.response;
using AdhdTimeOrganizer.application.dto.response.@base;
using AdhdTimeOrganizer.application.helper;
using AdhdTimeOrganizer.domain.model.entityInterface;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Humanizer;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.@base.read.pageFilterSort;

public abstract class BaseGridEndpoint<TEntity, TResponse, TFilter>(AppDbContext dbContext)
    : Endpoint<BaseFilterSortPaginateRequest<TFilter>, BaseGridResponse<TResponse>>
    where TEntity : class, IEntityWithId
    where TResponse : class, IIdResponse, IProjectionResponse<TResponse, TEntity>
    where TFilter : class, IFilterRequest
{
    public virtual string[] AllowedRoles() => EndpointHelper.GetAdminOrHigherRoles();

    public virtual string EndpointPath => "filtered-table";

    public override void Configure()
    {
        var entityName = typeof(TEntity).Name;
        Post($"/{entityName.Kebaberize()}/{EndpointPath}");
        Roles(AllowedRoles());

        Summary(s =>
        {
            s.Summary = $"Get filtered and paginated {entityName} list";
            s.Description = $"Retrieves a filtered, paginated and sorted list of {entityName}";

            s.Response<BaseGridResponse<TResponse>>(200, "Success");
            s.Response(400, "Bad request");
        });
    }

    public override async Task HandleAsync(BaseFilterSortPaginateRequest<TFilter> req, CancellationToken ct)
    {
        try
        {
            var query = dbContext.Set<TEntity>().AsNoTracking();

            if (req is { UseFilter: true, Filter: not null })
            {
                query = ApplyCustomFiltering(query, req.Filter);
            }

            var response = await query.GetGridDataAsync(
                req.SortBy,
                req.ItemsPerPage,
                req.Page,
                TResponse.Projection,
                ct);

            await Send.OkAsync(response, ct);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error retrieving table data for {Entity}", typeof(TEntity).Name);
            AddError("An internal error occurred.");
            await Send.ErrorsAsync(500, ct);
        }
    }

    protected abstract IQueryable<TEntity> ApplyCustomFiltering(IQueryable<TEntity> query, TFilter filter);
}