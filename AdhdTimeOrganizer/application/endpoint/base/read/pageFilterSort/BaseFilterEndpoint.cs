using AdhdTimeOrganizer.application.dto.request.generic;
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

public abstract class BaseFilterEndpoint<TEntity, TResponse, TFilter, TMapper>(
    AppDbContext dbContext,
    TMapper mapper) : Endpoint<TFilter, List<TResponse>>
    where TEntity : class, IEntityWithUser
    where TResponse : class, IIdResponse
    where TFilter : class, IFilterRequest
    where TMapper : class, IBaseResponseMapper<TEntity, TResponse>
{
    public virtual string EndpointPath => "filter";



    public virtual bool FilteredByUser => true;

    public virtual SortByRequest[] AlwaysSortBy => [];

    public override void Configure()
    {
        var entityName = typeof(TEntity).Name;
        Post($"/{entityName.Kebaberize()}/{EndpointPath}");
        
        Summary(s =>
        {
            s.Summary = $"Get filtered {entityName} list";
            s.Description = $"Retrieves a filtered list of {entityName}";

            s.Response<List<TResponse>>(200, "Success");
            s.Response(400, "Bad request");
        });
    }

    public override async Task HandleAsync(TFilter filter, CancellationToken ct)
    {
        try
        {
            var query = WithIncludes(dbContext.Set<TEntity>().AsNoTracking());

            if (FilteredByUser)
            {
                query = query.FilteredByUser(User.GetId());
            }

            query = ApplyCustomFiltering(query, filter);

            if (AlwaysSortBy.Length > 0)
            {
                query = query.SortByMany(AlwaysSortBy);
            }

            var response = await mapper.ProjectToResponse(query).ToListAsync(ct);

            await Send.OkAsync(response, ct);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error retrieving filtered data for {Entity}", typeof(TEntity).Name);
            AddError("An internal error occurred.");
            await Send.ErrorsAsync(500, ct);
        }
    }

    protected abstract IQueryable<TEntity> ApplyCustomFiltering(IQueryable<TEntity> query, TFilter filter);

    protected virtual IQueryable<TEntity> WithIncludes(IQueryable<TEntity> query)
    {
        return query;
    }
}