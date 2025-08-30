using AdhdTimeOrganizer.application.dto.request.@base.table;
using AdhdTimeOrganizer.application.dto.response.@base;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.application.mapper.@interface;
using AdhdTimeOrganizer.domain.helper;
using AdhdTimeOrganizer.domain.model.entity.user;
using AdhdTimeOrganizer.domain.model.entityInterface;
using AdhdTimeOrganizer.infrastructure.extension;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Humanizer;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.@base.read;

public abstract class BasePaginatedEndpoint<TEntity, TResponse, TMapper>(
    AppCommandDbContext dbContext,
    TMapper mapper) : Endpoint<SortPaginateRequest, BaseTableResponse<TResponse>>
    where TEntity : class, IEntityWithUser
    where TResponse : class, IIdResponse
    where TMapper : IBaseReadMapper<TEntity, TResponse>
{
    private readonly TMapper _mapper = mapper;

    public virtual string[] AllowedRoles()
    {
        return EndpointHelper.GetUserOrHigherRoles();
    }
    public virtual bool FilteredByUser => true;

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

            if (FilteredByUser)
            {
                query.FilteredByUser(User.GetId());
            }
            // Use utility to get paginated and sorted data
            var response = await query.GetTableDataAsync(
                req.SortBy,
                req.ItemsPerPage,
                req.Page,
                entity => _mapper.ToResponse(entity),
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