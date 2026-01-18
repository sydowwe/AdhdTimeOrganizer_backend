using AdhdTimeOrganizer.application.dto.request.@base.table;
using AdhdTimeOrganizer.application.dto.response.@base;
using AdhdTimeOrganizer.application.helper;
using AdhdTimeOrganizer.application.mapper.@interface;
using AdhdTimeOrganizer.domain.model.entityInterface;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Humanizer;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.@base.read.withoutUser;

public abstract class BaseWithoutUserPaginatedEndpoint<TEntity, TResponse, TMapper>(
    AppCommandDbContext dbContext,
    TMapper mapper) : Endpoint<SortPaginateRequest, BaseTableResponse<TResponse>>
    where TEntity : class, IEntityWithId
    where TResponse : class, IIdResponse
    where TMapper : class, IBaseReadMapper<TEntity, TResponse>
{

    public virtual string[] AllowedRoles()
    {
        return EndpointHelper.GetUserOrHigherRoles();
    }

    public override void Configure()
    {
        var entityName = typeof(TEntity).Name;
        Post($"/{entityName.Kebaberize()}/table");
        Roles(AllowedRoles());
        Summary(s =>
        {
            s.Summary = $"Get paginated {entityName} list";
            s.Description = $"Retrieves a paginated and sorted list of {entityName}";
            s.Response<BaseTableResponse<TResponse>>(200, "Success");
            s.Response(400, "Bad request");
        });
    }

    public override async Task HandleAsync(SortPaginateRequest req, CancellationToken ct)
    {
        try
        {
            var query = dbContext.Set<TEntity>().AsNoTracking();

            // Use utility to get paginated and sorted data
            var response = await query.GetTableDataAsync<TResponse, TEntity, TMapper>(
                req.SortBy,
                req.ItemsPerPage,
                req.Page,
                mapper,
                ct);

            await SendOkAsync(response, ct);
        }
        catch (Exception ex)
        {
            AddError($"An error occurred while retrieving data: {ex.Message}");
            await SendErrorsAsync(500, ct);
        }
    }
}