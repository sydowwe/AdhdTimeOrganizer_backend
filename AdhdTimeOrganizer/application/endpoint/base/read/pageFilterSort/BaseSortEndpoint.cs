using AdhdTimeOrganizer.application.dto.request.@base.table;
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

public abstract class BaseSortEndpoint<TEntity, TResponse, TMapper>(
    AppDbContext dbContext,
    TMapper mapper) : Endpoint<BaseSortRequest, List<TResponse>>
    where TEntity : class, IEntityWithUser
    where TResponse : class, IIdResponse
    where TMapper : class, IBaseReadMapper<TEntity, TResponse>
{
    public virtual string EndpointPath => "sort";

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
            s.Summary = $"Get sorted {entityName} list";
            s.Description = $"Retrieves a sorted of {entityName}";
            Roles(AllowedRoles());

            s.Response<BaseTableResponse<TResponse>>(200, "Success");
            s.Response(400, "Bad request");
        });
    }

    public override async Task HandleAsync(BaseSortRequest req, CancellationToken ct)
    {
        try
        {
            var query = WithIncludes(dbContext.Set<TEntity>().AsNoTracking());

            if (FilteredByUser)
            {
                query = query.FilteredByUser(User.GetId());
            }

            var sortBy = AlwaysSortBy.Concat(req.SortBy).ToArray();

            var response = await mapper.ProjectToResponse(query.SortByMany(sortBy)).ToListAsync(ct);

            await SendOkAsync(response, ct);
        }
        catch (Exception ex)
        {
            AddError($"An error occurred while retrieving filtered data: {ex.Message}");
            await SendErrorsAsync(500, ct);
        }
    }

    public virtual SortByRequest[] AlwaysSortBy => [];

    protected virtual IQueryable<TEntity> WithIncludes(IQueryable<TEntity> query)
    {
        return query;
    }

    protected virtual IQueryable<TResponse> ProjectToResponse(IQueryable<TEntity> query)
    {
        return mapper.ProjectToResponse(query);
    }
}